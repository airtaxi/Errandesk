using System.Net;
using System.Text.Json.Nodes;
using System.Web;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Plays the Microsoft identity platform device code flow and the Microsoft Graph To Do endpoints over a recording HTTP handler, serving collections in pages of <see cref="PageSize"/>.
/// </summary>
public sealed class FakeMicrosoftTodo
{
    public const string DeviceCode = "device-code-123";
    public const string UserCode = "ABCD-EFGH";
    public const string VerificationUri = "https://microsoft.com/devicelogin";
    public const string FirstAccessToken = "first-access-token";
    public const string RefreshedAccessToken = "refreshed-access-token";
    public const string RefreshToken = "refresh-token";

    private const string GraphPrefix = "/v1.0/me/todo/lists";

    private readonly Lock _gate = new();
    private int _remainingPendingPolls;
    private string _validAccessToken = FirstAccessToken;

    public FakeMicrosoftTodo() => Handler = new RecordingHttpMessageHandler(Respond);

    public RecordingHttpMessageHandler Handler { get; }

    public List<FakeTodoList> Lists { get; } = [];

    public int PageSize { get; set; } = 100;

    /// <summary>How many token requests answer <c>authorization_pending</c> before the user counts as signed in.</summary>
    public int PendingPolls
    {
        get => _remainingPendingPolls;
        set => _remainingPendingPolls = value;
    }

    /// <summary>When set, every device code token request fails with this OAuth error, such as <c>authorization_declined</c>.</summary>
    public string? TokenError { get; set; }

    /// <summary>When set, Graph rejects the first access token after this many Graph requests, so the import must refresh it.</summary>
    public int? ExpireFirstAccessTokenAfterRequests { get; set; }

    public IReadOnlyList<RecordedHttpRequest> GraphRequests =>
    [.. Handler.Requests.Where(request => request.Uri.Host == "graph.microsoft.com")];

    public IReadOnlyList<RecordedHttpRequest> IdentityRequests =>
    [.. Handler.Requests.Where(request => request.Uri.Host == "login.microsoftonline.com")];

    public FakeTodoList AddList(string id, string displayName, string wellknownListName = "none")
    {
        var list = new FakeTodoList(id, displayName, wellknownListName);
        Lists.Add(list);
        return list;
    }

    private HttpResponseMessage Respond(RecordedHttpRequest request)
    {
        lock (_gate)
        {
            return request.Uri.Host switch
            {
                "login.microsoftonline.com" => RespondToIdentity(request),
                "graph.microsoft.com" => RespondToGraph(request),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }
    }

    private HttpResponseMessage RespondToIdentity(RecordedHttpRequest request)
    {
        var form = HttpUtility.ParseQueryString(request.Body);
        if (request.Uri.AbsolutePath.EndsWith("/oauth2/v2.0/devicecode", StringComparison.Ordinal))
        {
            return RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.OK, new JsonObject { ["device_code"] = DeviceCode, ["user_code"] = UserCode, ["verification_uri"] = VerificationUri, ["expires_in"] = 900, ["interval"] = 5, ["message"] = "Sign in." }.ToJsonString());
        }

        if (form["grant_type"] == "refresh_token")
        {
            _validAccessToken = RefreshedAccessToken;
            return CreateTokenResponse(RefreshedAccessToken);
        }

        if (TokenError is not null) return CreateErrorResponse(TokenError);
        if (_remainingPendingPolls > 0)
        {
            _remainingPendingPolls--;
            return CreateErrorResponse("authorization_pending");
        }
        return CreateTokenResponse(FirstAccessToken);
    }

    private HttpResponseMessage RespondToGraph(RecordedHttpRequest request)
    {
        var graphRequestCount = GraphRequests.Count;
        if (ExpireFirstAccessTokenAfterRequests is { } expiryCount && graphRequestCount > expiryCount && _validAccessToken == FirstAccessToken) _validAccessToken = "";
        if (!request.Headers.TryGetValue("Authorization", out var authorization) || authorization != $"Bearer {_validAccessToken}") return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var path = request.Uri.AbsolutePath;
        if (!path.StartsWith(GraphPrefix, StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NotFound);

        var segments = path[GraphPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var skip = int.TryParse(HttpUtility.ParseQueryString(request.Uri.Query)["$skiptoken"], out var skipToken) ? skipToken : 0;
        return segments switch
        {
            [] => CreatePage(request.Uri, [.. Lists.Select(list => new JsonObject { ["id"] = list.Id, ["displayName"] = list.DisplayName, ["isOwner"] = true, ["isShared"] = false, ["wellknownListName"] = list.WellknownListName })], skip),
            [var listId, "tasks"] when FindList(listId) is { } list => CreatePage(request.Uri, [.. list.Tasks.Select(task => task.Json)], skip),
            [var listId, "tasks", var taskId, "checklistItems"] when FindList(listId)?.Tasks.Find(task => task.Id == taskId) is { } task => CreatePage(request.Uri, task.ChecklistItems, skip),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private FakeTodoList? FindList(string listId) => Lists.Find(list => list.Id == listId);

    private HttpResponseMessage CreatePage(Uri requestUri, IReadOnlyList<JsonObject> items, int skip)
    {
        var page = new JsonObject { ["value"] = new JsonArray([.. items.Skip(skip).Take(PageSize).Select(item => item.DeepClone())]) };
        if (skip + PageSize < items.Count) page["@odata.nextLink"] = $"https://graph.microsoft.com{requestUri.AbsolutePath}?$skiptoken={skip + PageSize}";
        return RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.OK, page.ToJsonString());
    }

    private static HttpResponseMessage CreateTokenResponse(string accessToken) => RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.OK, new JsonObject { ["token_type"] = "Bearer", ["scope"] = "Tasks.Read", ["expires_in"] = 3599, ["access_token"] = accessToken, ["refresh_token"] = RefreshToken }.ToJsonString());

    private static HttpResponseMessage CreateErrorResponse(string error) => RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.BadRequest, new JsonObject { ["error"] = error, ["error_description"] = error }.ToJsonString());
}
