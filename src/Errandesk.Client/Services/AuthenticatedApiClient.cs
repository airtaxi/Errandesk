using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Client.Managers;
using Errandesk.Contracts;

namespace Errandesk.Client.Services;

/// <summary>
/// Calls the endpoints of the Errandesk server API that need a signed-in user and are not part of the synchronized data. The HTTP client carries <see cref="AuthenticationHandler"/>.
/// </summary>
/// <remarks>
/// Every method throws <see cref="ApiException"/> for error statuses, <see cref="HttpRequestException"/> when the server cannot be reached,
/// <see cref="TaskCanceledException"/> on timeouts and <see cref="SessionExpiredException"/> when the session ended.
/// </remarks>
public sealed class AuthenticatedApiClient(HttpClient httpClient, SessionManager sessionManager)
{
    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    public Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken) => GetAsync("api/users/me", JsonContext.UserResponse, cancellationToken);

    public Task<UserResponse> UpdateCurrentUserAsync(UpdateUserRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Patch, "api/users/me", request, JsonContext.UpdateUserRequest, JsonContext.UserResponse, cancellationToken);

    public Task<IReadOnlyList<ConversationResponse>> GetConversationsAsync(CancellationToken cancellationToken) => GetAsync("api/conversations", JsonContext.IReadOnlyListConversationResponse, cancellationToken);

    public Task<ConversationResponse> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken) => GetAsync($"api/conversations/{conversationId}", JsonContext.ConversationResponse, cancellationToken);

    public Task<ConversationResponse> CreateConversationAsync(CreateConversationRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "api/conversations", request, JsonContext.CreateConversationRequest, JsonContext.ConversationResponse, cancellationToken);

    public Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken) => DeleteAsync($"api/conversations/{conversationId}", cancellationToken);

    public Task<IReadOnlyList<ConversationMessageResponse>> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => GetAsync($"api/conversations/{conversationId}/messages", JsonContext.IReadOnlyListConversationMessageResponse, cancellationToken);

    /// <summary>
    /// Uploads an image for the next message of a conversation.
    /// </summary>
    /// <exception cref="ApiException">400 when the image is too large or not a supported format.</exception>
    public async Task<ConversationAttachmentResponse> UploadConversationAttachmentAsync(Guid conversationId, byte[] data, string mediaType, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, CreateUri($"api/conversations/{conversationId}/attachments")) { Content = content };
        using var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        return await ApiResponseReader.ReadAsync(response, JsonContext.ConversationAttachmentResponse, cancellationToken);
    }

    /// <summary>
    /// Downloads the image file of a conversation attachment.
    /// </summary>
    public async Task<byte[]> GetConversationAttachmentAsync(Guid conversationId, Guid attachmentId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(CreateUri($"api/conversations/{conversationId}/attachments/{attachmentId}"), cancellationToken);
        await ApiResponseReader.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Restores the items an assistant action changed to their state before the action.
    /// </summary>
    /// <exception cref="ApiException">409 when the action was already undone or an affected item changed since.</exception>
    public Task<AgentActionResponse> UndoAgentActionAsync(Guid agentActionId, CancellationToken cancellationToken) => SendWithoutBodyAsync(HttpMethod.Post, $"api/agent-actions/{agentActionId}/undo", JsonContext.AgentActionResponse, cancellationToken);

    /// <summary>
    /// Registers this device, or updates it when its ID is already registered.
    /// </summary>
    public Task<DeviceResponse> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "api/devices", request, JsonContext.RegisterDeviceRequest, JsonContext.DeviceResponse, cancellationToken);

    public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => DeleteAsync($"api/devices/{deviceId}", cancellationToken);

    /// <summary>
    /// Shares a list with the user who has exactly this user name, and returns the list with its members.
    /// </summary>
    /// <exception cref="ApiException">404 when no user or list matches, 400 for the owner's own name or the default list, 403 when the caller does not own the list, 429 after too many attempts.</exception>
    public Task<TaskListResponse> AddTaskListMemberAsync(Guid listId, AddTaskListMemberRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, $"api/lists/{listId}/members", request, JsonContext.AddTaskListMemberRequest, JsonContext.TaskListResponse, cancellationToken);

    /// <summary>
    /// Removes a member from a list. A member passes their own ID to leave the list.
    /// </summary>
    /// <exception cref="ApiException">404 when the list or the member is not found, 403 when a member removes someone else.</exception>
    public Task RemoveTaskListMemberAsync(Guid listId, Guid userId, CancellationToken cancellationToken) => DeleteAsync($"api/lists/{listId}/members/{userId}", cancellationToken);

    public Task<IReadOnlyList<InviteResponse>> GetInvitesAsync(CancellationToken cancellationToken) => GetAsync("api/admin/invites", JsonContext.IReadOnlyListInviteResponse, cancellationToken);

    public Task<InviteResponse> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "api/admin/invites", request, JsonContext.CreateInviteRequest, JsonContext.InviteResponse, cancellationToken);

    public Task DeleteInviteAsync(Guid inviteId, CancellationToken cancellationToken) => DeleteAsync($"api/admin/invites/{inviteId}", cancellationToken);

    /// <summary>
    /// Starts a Microsoft To Do import and returns the job with the sign-in code.
    /// </summary>
    /// <exception cref="ApiException">409 when an import is already running, 503 when the server cannot import or Microsoft sign-in cannot be reached.</exception>
    public Task<ImportJobResponse> StartMicrosoftTodoImportAsync(CancellationToken cancellationToken) => SendWithoutBodyAsync(HttpMethod.Post, "api/import/microsoft-todo", JsonContext.ImportJobResponse, cancellationToken);

    public Task<ImportJobResponse> GetImportJobAsync(Guid jobId, CancellationToken cancellationToken) => GetAsync($"api/import/jobs/{jobId}", JsonContext.ImportJobResponse, cancellationToken);

    public Task<IReadOnlyList<PersonalAccessTokenResponse>> GetPersonalAccessTokensAsync(CancellationToken cancellationToken) => GetAsync("api/users/me/tokens", JsonContext.IReadOnlyListPersonalAccessTokenResponse, cancellationToken);

    /// <summary>
    /// Creates a personal access token. The response holds the secret token value, which the server never returns again.
    /// </summary>
    public Task<CreatedPersonalAccessTokenResponse> CreatePersonalAccessTokenAsync(CreatePersonalAccessTokenRequest request, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "api/users/me/tokens", request, JsonContext.CreatePersonalAccessTokenRequest, JsonContext.CreatedPersonalAccessTokenResponse, cancellationToken);

    public Task RevokePersonalAccessTokenAsync(Guid tokenId, CancellationToken cancellationToken) => DeleteAsync($"api/users/me/tokens/{tokenId}", cancellationToken);

    /// <summary>
    /// The most recent morning briefing, or <see langword="null"/> when none was written yet.
    /// </summary>
    public async Task<MorningBriefingResponse?> GetLatestMorningBriefingAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(CreateUri("api/briefings/latest"), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await ApiResponseReader.ReadAsync(response, JsonContext.MorningBriefingResponse, cancellationToken);
    }

    private async Task<TResponse> GetAsync<TResponse>(string path, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(CreateUri(path), cancellationToken);
        return await ApiResponseReader.ReadAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest request, JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(method, CreateUri(path)) { Content = JsonContent.Create(request, requestTypeInfo) };
        using var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        return await ApiResponseReader.ReadAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task<TResponse> SendWithoutBodyAsync<TResponse>(HttpMethod method, string path, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(method, CreateUri(path));
        using var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        return await ApiResponseReader.ReadAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(CreateUri(path), cancellationToken);
        await ApiResponseReader.EnsureSuccessAsync(response, cancellationToken);
    }

    private Uri CreateUri(string path) => new(sessionManager.ServerAddress, path);
}
