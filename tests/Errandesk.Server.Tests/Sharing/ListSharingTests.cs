using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;
using static Errandesk.Server.Tests.Sharing.SharingTestHelpers;

namespace Errandesk.Server.Tests.Sharing;

public sealed class ListSharingTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task Share_ByExactUserNameIgnoringCase_LetsTheMemberReadAndEditTheTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var groceries = await CreateListAsync(alice, "Groceries");
        var milk = await CreateTaskAsync(alice, groceries.Id, "Buy milk", isImportant: true);

        var shared = await ShareAsync(alice, groceries.Id, "  BOB ");

        Assert.Equal(alice.Id, shared.Owner!.UserId);
        Assert.Equal("alice", shared.Owner.UserName);
        var member = Assert.Single(shared.Members!);
        Assert.Equal(bob.Id, member.UserId);
        Assert.Equal("bob", member.UserName);
        Assert.Equal("bob", member.DisplayName);
        Assert.Equal(ApiTestServer.DefaultStartTime, member.AddedAt);
        Assert.True(shared.ChangeVersion > groceries.ChangeVersion);

        var bobsLists = await bob.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath);
        Assert.Equal(["Tasks", "Groceries"], bobsLists.Select(list => list.Name));
        JsonAssert.Equal(shared, Assert.Single(bobsLists, list => list.Id == groceries.Id));
        JsonAssert.Equal(shared, await bob.Client.GetAsync<TaskListResponse>($"{ListsPath}/{groceries.Id}"));
        Assert.Equal([milk.Id], (await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={groceries.Id}")).Select(task => task.Id));
        Assert.Contains(milk.Id, (await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.Important}")).Select(task => task.Id));
        Assert.Contains(milk.Id, (await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath)).Select(task => task.Id));

        await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}", new UpdateTaskRequest { Title = "Buy oat milk", DueDate = new DateOnly(2031, 3, 12) });
        await bob.Client.PostAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}/steps", new CreateTaskStepRequest("Check the price"));
        var completed = await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}", new UpdateTaskRequest { IsCompleted = true });
        var bread = await CreateTaskAsync(bob, groceries.Id, "Buy bread");

        var milkForAlice = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}");
        Assert.Equal("Buy oat milk", milkForAlice.Title);
        Assert.True(milkForAlice.IsCompleted);
        Assert.Equal("Check the price", Assert.Single(milkForAlice.Steps).Title);
        Assert.Equal(completed.ChangeVersion, milkForAlice.ChangeVersion);
        Assert.Equal(bread.Id, (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{bread.Id}")).Id);
        Assert.Equal(alice.Id, (await server.FindRawAsync(server.Database.Tasks, bread.Id)).OwnerUserId);

        await bob.Client.DeleteAsync($"{TasksPath}/{bread.Id}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{bread.Id}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyTheOwner_RenamesDeletesAndSharesTheList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var carol = await server.RegisterInvitedAsync(alice, "carol");
        var list = await CreateListAsync(alice, "Trip");
        await ShareAsync(alice, list.Id, "bob");
        var shared = await ShareAsync(alice, list.Id, "carol");
        var listPath = $"{ListsPath}/{list.Id}";

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, listPath, new UpdateTaskListRequest { Name = "Bob's trip" }, HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, listPath, null, HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("alice"), HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{carol.Id}", null, HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{alice.Id}", null, HttpStatusCode.Forbidden);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{alice.Id}", null, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("alice"), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest(" "), HttpStatusCode.BadRequest);
        var defaultList = await server.GetDefaultListAsync(alice);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(defaultList.Id), new AddTaskListMemberRequest("bob"), HttpStatusCode.BadRequest);

        var repeated = await ShareAsync(alice, list.Id, "bob");
        JsonAssert.Equal(shared, repeated);
        Assert.Equal([bob.Id, carol.Id], repeated.Members!.Select(member => member.UserId));

        var renamed = await alice.Client.PatchAsync<TaskListResponse>(listPath, new UpdateTaskListRequest { Name = "Summer trip" });
        Assert.Equal("Summer trip", (await bob.Client.GetAsync<TaskListResponse>(listPath)).Name);
        Assert.Equal(2, renamed.Members!.Count);
        await alice.Client.DeleteAsync($"{MembersPath(list.Id)}/{carol.Id}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{carol.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.DeleteAsync(listPath);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, listPath, null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UsersWhoAreNotMembers_SeeNothingOfTheList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var dave = await server.RegisterInvitedAsync(alice, "dave");
        var list = await CreateListAsync(alice, "Family");
        var task = await CreateTaskAsync(alice, list.Id, "Plan the party");
        await ShareAsync(alice, list.Id, "bob");
        var taskPath = $"{TasksPath}/{task.Id}";

        await dave.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ListsPath}/{list.Id}", null, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}?view={list.Id}", null, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Get, taskPath, null, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Patch, taskPath, new UpdateTaskRequest { Title = "Mine" }, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{taskPath}/steps", new CreateTaskStepRequest("Step"), HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Delete, taskPath, null, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Sneak in", ListId: list.Id), HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("dave"), HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{dave.Id}", null, HttpStatusCode.NotFound);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{MembersPath(list.Id)}/{bob.Id}", null, HttpStatusCode.NotFound);
        Assert.DoesNotContain(list.Id, (await dave.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath)).Select(visible => visible.Id));
        Assert.Empty(await dave.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
        var daveChanges = await GetChangesAsync(dave, 0);
        Assert.DoesNotContain(list.Id, daveChanges.Lists.Select(changed => changed.Id));
        Assert.Empty(daveChanges.Tasks);

        var daveNote = await dave.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Party"), HttpStatusCode.Created);
        await dave.Client.SendExpectingProblemAsync(HttpMethod.Put, $"/api/notes/{daveNote.Id}/linked-tasks/{task.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal("Plan the party", (await alice.Client.GetAsync<TaskItemResponse>(taskPath)).Title);
    }

    [Fact]
    public async Task Members_CanLeave_AndRemovedMembersLoseAccess()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var carol = await server.RegisterInvitedAsync(alice, "carol");
        var list = await CreateListAsync(alice, "Chores");
        var task = await CreateTaskAsync(alice, list.Id, "Vacuum");
        await ShareAsync(alice, list.Id, "bob");
        await ShareAsync(alice, list.Id, "carol");

        await RemoveMemberAsync(bob, list.Id, bob.Id);
        await alice.Client.DeleteAsync($"{MembersPath(list.Id)}/{carol.Id}");

        foreach (var formerMember in new[] { bob, carol })
        {
            await formerMember.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ListsPath}/{list.Id}", null, HttpStatusCode.NotFound);
            await formerMember.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
            await formerMember.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true }, HttpStatusCode.NotFound);
            Assert.DoesNotContain(list.Id, (await formerMember.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath)).Select(visible => visible.Id));
        }

        var current = await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{list.Id}");
        Assert.Empty(current.Members!);
        var stored = await server.FindRawAsync(server.Database.TaskLists, list.Id);
        Assert.Equal([bob.Id, carol.Id], stored.FormerMemberUserIds);

        var reshared = await ShareAsync(alice, list.Id, "bob");
        Assert.Equal(bob.Id, Assert.Single(reshared.Members!).UserId);
        Assert.Equal([carol.Id], (await server.FindRawAsync(server.Database.TaskLists, list.Id)).FormerMemberUserIds);
        Assert.Equal("Vacuum", (await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).Title);
    }

    [Fact]
    public async Task MyDay_IsPersonalInSharedLists()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var bob = await server.RegisterInvitedAsync(alice, "bob", timeZone: "America/Los_Angeles");
        var list = await CreateListAsync(alice, "Household");
        await ShareAsync(alice, list.Id, "bob");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants", ListId: list.Id, IsInMyDay: true), HttpStatusCode.Created);
        var bobsTask = await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Fix the tap", ListId: list.Id, IsInMyDay: true), HttpStatusCode.Created);

        Assert.True(task.IsInMyDay);
        Assert.Equal(new DateOnly(2031, 3, 10), task.MyDayDate);
        Assert.False((await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).IsInMyDay);
        Assert.True(bobsTask.IsInMyDay);
        Assert.Equal(new DateOnly(2031, 3, 10), bobsTask.MyDayDate);
        Assert.False((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{bobsTask.Id}")).IsInMyDay);

        var addedForBob = await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsInMyDay = true });
        Assert.True(addedForBob.IsInMyDay);
        Assert.Equal(new[] { bobsTask.Id, task.Id }.Order(), (await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}")).Select(myDayTask => myDayTask.Id).Order());
        Assert.Equal([task.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}")).Select(myDayTask => myDayTask.Id));

        await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsInMyDay = false });
        Assert.True((await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).IsInMyDay);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}"));

        var stored = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.Null(stored.MyDayDate);
        Assert.Equal(bob.Id, Assert.Single(stored.MemberMyDayDates).UserId);
        var bobsChanges = await GetChangesAsync(bob, 0);
        Assert.True(Assert.Single(bobsChanges.Tasks, changed => changed.Id == task.Id).IsInMyDay);
    }

    [Fact]
    public async Task Notes_StayPrivateWhenLinkedToSharedTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Wedding");
        var task = await CreateTaskAsync(alice, list.Id, "Book the venue");
        await ShareAsync(alice, list.Id, "bob");

        var bobsNote = await bob.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Venue ideas", "Garden", LinkedTaskIds: [task.Id]), HttpStatusCode.Created);

        Assert.Equal([task.Id], bobsNote.LinkedTaskIds);
        Assert.Equal(bobsNote.Id, Assert.Single((await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).LinkedNotes).Id);
        Assert.Empty((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).LinkedNotes);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/notes/{bobsNote.Id}", null, HttpStatusCode.NotFound);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"/api/notes?linkedTaskId={task.Id}"));
        Assert.Empty((await GetChangesAsync(alice, 0)).Notes);

        await RemoveMemberAsync(alice, list.Id, bob.Id);
        Assert.Empty((await bob.Client.GetAsync<NoteResponse>($"/api/notes/{bobsNote.Id}")).LinkedTaskIds);
    }

    [Fact]
    public async Task MovingATask_ToAListOthersCannotSee_TakesItAwayFromThem()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var shared = await CreateListAsync(alice, "Shared");
        var alicesPrivate = await CreateListAsync(alice, "Private");
        var bobsPrivate = await CreateListAsync(bob, "Bob's");
        var gift = await CreateTaskAsync(alice, shared.Id, "Buy a gift for Bob");
        var tools = await CreateTaskAsync(alice, shared.Id, "Borrow tools");
        await ShareAsync(alice, shared.Id, "bob");
        var aliceSince = await GetCurrentVersionAsync(alice);
        var bobSince = await GetCurrentVersionAsync(bob);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob);

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{tools.Id}", new UpdateTaskRequest { ListId = alicesPrivate.Id }, HttpStatusCode.NotFound);
        var moved = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{gift.Id}", new UpdateTaskRequest { ListId = alicesPrivate.Id });
        var taken = await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{tools.Id}", new UpdateTaskRequest { ListId = bobsPrivate.Id, IsInMyDay = true });

        Assert.Equal(alicesPrivate.Id, moved.ListId);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{gift.Id}", null, HttpStatusCode.NotFound);
        var bobsChanges = await GetChangesAsync(bob, bobSince);
        Assert.Contains(gift.Id, bobsChanges.DeletedTasks.Select(tombstone => tombstone.Id));
        Assert.DoesNotContain(gift.Id, bobsChanges.Tasks.Select(changed => changed.Id));
        Assert.Equal([bob.Id], (await server.FindRawAsync(server.Database.Tasks, gift.Id)).RevokedUserIds);

        Assert.True(taken.IsInMyDay);
        var storedTools = await server.FindRawAsync(server.Database.Tasks, tools.Id);
        Assert.Equal(bob.Id, storedTools.OwnerUserId);
        Assert.Equal([alice.Id], storedTools.RevokedUserIds);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{tools.Id}", null, HttpStatusCode.NotFound);
        var alicesChanges = await GetChangesAsync(alice, aliceSince);
        Assert.Contains(tools.Id, alicesChanges.DeletedTasks.Select(tombstone => tombstone.Id));
        Assert.Equal(alicesPrivate.Id, Assert.Single(alicesChanges.Tasks, changed => changed.Id == gift.Id).ListId);

        var returned = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{gift.Id}", new UpdateTaskRequest { ListId = shared.Id });
        Assert.Empty((await server.FindRawAsync(server.Database.Tasks, gift.Id)).RevokedUserIds);
        JsonAssert.Equal(returned, await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{gift.Id}"));
    }

    [Fact]
    public async Task Create_WithTheIdOfATaskTheUserCannotSee_Conflicts()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var shared = await CreateListAsync(alice, "Shared");
        var alicesPrivate = await CreateListAsync(alice, "Private");
        var secret = await CreateTaskAsync(alice, alicesPrivate.Id, "Surprise party");
        await ShareAsync(alice, shared.Id, "bob");

        var problem = await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Guess", Id: secret.Id, ListId: shared.Id), HttpStatusCode.Conflict);

        Assert.DoesNotContain("Surprise", problem.GetRawText());
        var ownId = Guid.CreateVersion7();
        var created = await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Mine", Id: ownId, ListId: shared.Id), HttpStatusCode.Created);
        JsonAssert.Equal(created, await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Mine", Id: ownId, ListId: shared.Id)));
    }

    [Fact]
    public async Task DeletingASharedList_RemovesItForEveryMember()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Old project");
        var task = await CreateTaskAsync(alice, list.Id, "Archive files");
        await ShareAsync(alice, list.Id, "bob");
        var bobSince = await GetCurrentVersionAsync(bob);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob);

        await alice.Client.DeleteAsync($"{ListsPath}/{list.Id}");

        var changes = await GetChangesAsync(bob, bobSince);
        Assert.Equal(list.Id, Assert.Single(changes.DeletedLists).Id);
        Assert.Equal(task.Id, Assert.Single(changes.DeletedTasks).Id);
        Assert.Empty(changes.Lists);
        Assert.Empty(changes.Tasks);
    }
}
