using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Sharing;

/// <summary>
/// Requests shared by the list sharing tests.
/// </summary>
public static class SharingTestHelpers
{
    public const string ListsPath = "/api/lists";
    public const string TasksPath = "/api/tasks";
    public const string ChangesPath = "/api/changes";

    public static Task<TaskListResponse> CreateListAsync(TestUser owner, string name) => owner.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest(name), HttpStatusCode.Created);

    public static Task<TaskItemResponse> CreateTaskAsync(TestUser user, Guid listId, string title, bool isImportant = false) => user.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest(title, ListId: listId, IsImportant: isImportant), HttpStatusCode.Created);

    public static Task<TaskListResponse> ShareAsync(TestUser owner, Guid listId, string userName) => owner.Client.PostAsync<TaskListResponse>(MembersPath(listId), new AddTaskListMemberRequest(userName));

    public static Task RemoveMemberAsync(TestUser user, Guid listId, Guid memberUserId) => user.Client.DeleteAsync($"{MembersPath(listId)}/{memberUserId}");

    public static async Task<long> GetCurrentVersionAsync(TestUser user) => (await user.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={long.MaxValue}")).CurrentVersion;

    public static Task<ChangesResponse> GetChangesAsync(TestUser user, long since) => user.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={since}");

    public static string MembersPath(Guid listId) => $"{ListsPath}/{listId}/members";
}
