using System.Security.Cryptography;
using System.Text;
using Errandesk.Server.Authentication;
using Errandesk.Server.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace Errandesk.Server.Services;

/// <summary>
/// Makes authenticated <c>POST</c>, <c>PATCH</c> and <c>DELETE</c> requests that carry an <c>Idempotency-Key</c> header safe to repeat:
/// the first request runs and its response is stored, and later requests with the same key and the same method, path, query and body get the stored response without running again.
/// </summary>
/// <remarks>
/// Successful responses and client errors are stored, except 401, 408, 409 and 429, whose cause can go away; server errors are never stored.
/// Reusing a key for a different request answers 422; a key whose first request is still running answers 409.
/// </remarks>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    public const int MaximumKeyLength = 255;

    /// <summary>Responses larger than this are not stored, keeping records well below the MongoDB document size limit.</summary>
    public const int MaximumStoredResponseLength = 12 * 1024 * 1024;

    private static readonly TimeSpan s_retryAfter = TimeSpan.FromSeconds(1);

    public async Task InvokeAsync(HttpContext context, IdempotencyKeyStore store, IProblemDetailsService problemDetailsService, IEnumerable<IExceptionHandler> exceptionHandlers)
    {
        if (!IsApplicable(context, out var keyValues))
        {
            await next(context);
            return;
        }

        var cancellationToken = context.RequestAborted;
        if (!TryReadKey(keyValues, out var key))
        {
            await WriteProblemAsync(context, problemDetailsService, StatusCodes.Status400BadRequest, $"The {HeaderName} header must be one value of 1 to {MaximumKeyLength} visible ASCII characters.");
            return;
        }

        var userId = context.User.GetUserId();
        var fingerprint = await ComputeFingerprintAsync(context.Request, cancellationToken);
        var claim = await store.ClaimAsync(userId, key, fingerprint, cancellationToken);
        switch (claim.Outcome)
        {
            case IdempotencyClaimOutcome.Replay:
                await WriteStoredResponseAsync(context, claim.StoredResponse!, cancellationToken);
                return;
            case IdempotencyClaimOutcome.Mismatch:
                await WriteProblemAsync(context, problemDetailsService, StatusCodes.Status422UnprocessableEntity, $"This {HeaderName} was already used for a different request.");
                return;
            case IdempotencyClaimOutcome.InProgress:
                context.Response.Headers.RetryAfter = ((int)s_retryAfter.TotalSeconds).ToString();
                await WriteProblemAsync(context, problemDetailsService, StatusCodes.Status409Conflict, $"A request with this {HeaderName} is still being processed. Try again.");
                return;
        }

        await RunAndStoreAsync(context, store, exceptionHandlers, userId, key, claim.ClaimId);
    }

    private async Task RunAndStoreAsync(HttpContext context, IdempotencyKeyStore store, IEnumerable<IExceptionHandler> exceptionHandlers, Guid userId, string key, Guid claimId)
    {
        var response = context.Response;
        var originalBody = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        var isStored = false;
        try
        {
            try { await next(context); }
            catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested && !response.HasStarted)
            {
                if (!await TryHandleExceptionAsync(context, exception, exceptionHandlers))
                {
                    throw;
                }
            }

            if (IsStorable(response.StatusCode) && buffer.Length <= MaximumStoredResponseLength)
            {
                await store.CompleteAsync(userId, key, claimId, response.StatusCode, response.ContentType, response.Headers.Location.ToString() is { Length: > 0 } location ? location : null, buffer.ToArray(), CancellationToken.None);
                isStored = true;
            }
        }
        finally
        {
            response.Body = originalBody;
            if (!isStored) await store.ReleaseAsync(userId, key, claimId, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    private static bool IsApplicable(HttpContext context, out StringValues keyValues)
    {
        keyValues = default;
        var method = context.Request.Method;
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPatch(method) && !HttpMethods.IsDelete(method)) return false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out keyValues)) return false;
        if (context.User.Identity?.IsAuthenticated != true) return false;
        return context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyExemptAttribute>() is null;
    }

    private static bool TryReadKey(StringValues keyValues, out string key)
    {
        key = keyValues.Count == 1 ? keyValues[0] ?? "" : "";
        return key.Length is > 0 and <= MaximumKeyLength && key.All(character => character is > ' ' and <= '~');
    }

    private static bool IsStorable(int statusCode) => statusCode is >= 200 and < 500 and not StatusCodes.Status401Unauthorized and not StatusCodes.Status408RequestTimeout and not StatusCodes.Status409Conflict and not StatusCodes.Status429TooManyRequests;

    private static async Task<string> ComputeFingerprintAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method.ToUpperInvariant()}\n{request.Path}{request.QueryString}\n"));

        var chunk = new byte[16 * 1024];
        int readCount;
        while ((readCount = await request.Body.ReadAsync(chunk, cancellationToken)) > 0) hash.AppendData(chunk, 0, readCount);
        request.Body.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Lets the registered exception handlers write the response for <paramref name="exception"/> into the buffered body, as the exception handler middleware would.</summary>
    private static async Task<bool> TryHandleExceptionAsync(HttpContext context, Exception exception, IEnumerable<IExceptionHandler> exceptionHandlers)
    {
        context.Response.Clear();
        foreach (var exceptionHandler in exceptionHandlers)
        {
            if (await exceptionHandler.TryHandleAsync(context, exception, context.RequestAborted))
            {
                return true;
            }
        }
        context.Response.Clear();
        return false;
    }

    private static async Task WriteStoredResponseAsync(HttpContext context, IdempotencyKeyDocument storedResponse, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = storedResponse.StatusCode;
        if (storedResponse.ContentType is not null) response.ContentType = storedResponse.ContentType;
        if (storedResponse.Location is not null) response.Headers.Location = storedResponse.Location;
        response.Headers[ReplayedHeaderName] = "true";
        if (storedResponse.ResponseBody.Length > 0) await response.Body.WriteAsync(storedResponse.ResponseBody, cancellationToken);
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problemDetailsService, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = { Status = statusCode, Detail = detail } });
    }
}
