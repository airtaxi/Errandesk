using System.Globalization;
using System.Net;
using Errandesk.Client.Services;
using Errandesk.Client.Sync;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Turns failures of server requests into messages for the user.
/// </summary>
public static class ApiErrorMessages
{
    /// <summary>
    /// Whether <paramref name="exception"/> is an expected failure of a server request, or a change to an item no longer stored on this device, that the interface reports instead of crashing.
    /// </summary>
    public static bool IsRequestFailure(Exception exception) => exception is HttpRequestException or TaskCanceledException or ApiException or InvalidDataException or SessionExpiredException or LocalItemNotFoundException;

    /// <summary>
    /// The message for a failed request, or <see langword="null"/> when the session ended and the app returns to the sign-in page.
    /// </summary>
    public static string? GetMessage(Exception exception) => exception switch
    {
        SessionExpiredException => null,
        ApiException { StatusCode: HttpStatusCode.NotFound } or LocalItemNotFoundException => LocalizedStrings.Get("ItemNotFoundError"),
        ApiException { StatusCode: HttpStatusCode.TooManyRequests } => LocalizedStrings.Get("TooManyRequestsError"),
        ApiException { StatusCode: HttpStatusCode.ServiceUnavailable } => LocalizedStrings.Get("ServerUnavailableError"),
        ApiException { StatusCode: HttpStatusCode.BadRequest, ProblemDetail: { Length: > 0 } detail } => detail,
        ApiException apiException => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("RequestFailedError"), (int)apiException.StatusCode),
        HttpRequestException => LocalizedStrings.Get("ServerUnreachableError"),
        TaskCanceledException => LocalizedStrings.Get("ServerTimeoutError"),
        _ => LocalizedStrings.Get("ServerNotErrandeskError"),
    };
}
