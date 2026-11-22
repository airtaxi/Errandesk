using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Import;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Import;

public sealed class MicrosoftTodoImportTests(MongoDbContainerFixture mongoDb)
{
    private const string StartPath = "/api/import/microsoft-todo";
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ServerInfo_ReportsWhetherTheImportIsAvailable()
    {
        await using var configuredServer = await StartServerAsync(new FakeMicrosoftTodo());
        await using var unconfiguredServer = await ApiTestServer.StartAsync(mongoDb);

        var configuredInfo = await configuredServer.Anonymous.GetAsync<ServerInfoResponse>("/api/server-info");
        var unconfiguredInfo = await unconfiguredServer.Anonymous.GetAsync<ServerInfoResponse>("/api/server-info");

        Assert.True(configuredInfo.IsMicrosoftTodoImportAvailable);
        Assert.False(unconfiguredInfo.IsMicrosoftTodoImportAvailable);
    }

    [Fact]
    public async Task Start_WithoutMicrosoftGraphConfigured_Returns503()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var user = await server.RegisterAsync("alice");

        var problem = await user.Client.SendExpectingProblemAsync(HttpMethod.Post, StartPath, null, HttpStatusCode.ServiceUnavailable);

        Assert.Contains("Microsoft Graph", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Start_ReturnsTheSignInCode_ThenImportsAfterTheUserSignedIn()
    {
        var fake = new FakeMicrosoftTodo { PendingPolls = 2 };
        fake.AddList("default-list", "Tasks", "defaultList").Tasks.Add(FakeTodoTask.Create("task-1", "Buy milk"));
        await using var server = await StartServerAsync(fake, new Dictionary<string, string> { [ServerEnvironmentVariables.MicrosoftGraphTenant] = "consumers" });
        var user = await server.RegisterAsync("alice");

        var started = await user.Client.SendAsync<ImportJobResponse>(HttpMethod.Post, StartPath, null, HttpStatusCode.Created);

        Assert.Equal(ImportSources.MicrosoftTodo, started.Source);
        Assert.Equal(ImportJobStatus.WaitingForSignIn, started.Status);
        Assert.Equal(FakeMicrosoftTodo.UserCode, started.UserCode);
        Assert.Equal(FakeMicrosoftTodo.VerificationUri, started.VerificationUri);
        Assert.Equal(server.TimeProvider.GetUtcNow().AddSeconds(900), started.ExpiresAt);

        var finished = await WaitForJobAsync(server, user, started.Id);
        Assert.Equal(ImportJobStatus.Completed, finished.Status);
        Assert.Equal(1, finished.TasksImported);
        Assert.NotNull(finished.CompletedAt);

        var deviceCodeRequest = fake.IdentityRequests[0];
        Assert.Equal("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", deviceCodeRequest.Uri.ToString());
        var deviceCodeForm = HttpUtility.ParseQueryString(deviceCodeRequest.Body);
        Assert.Equal(ClientId, deviceCodeForm["client_id"]);
        Assert.Equal("Tasks.Read offline_access", deviceCodeForm["scope"]);

        var tokenRequests = fake.IdentityRequests.Skip(1).ToList();
        Assert.Equal(3, tokenRequests.Count);
        Assert.All(tokenRequests, request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            Assert.Equal(ClientId, form["client_id"]);
            Assert.Equal(FakeMicrosoftTodo.DeviceCode, form["device_code"]);
        });
        Assert.All(fake.GraphRequests, request => Assert.Equal($"Bearer {FakeMicrosoftTodo.FirstAccessToken}", request.Headers["Authorization"]));

        var storedJob = await server.Database.ImportJobs.Find(Builders<ImportJobDocument>.Filter.Eq(job => job.Id, started.Id)).As<BsonDocument>().SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeMicrosoftTodo.FirstAccessToken, storedJob.ToJson());
        Assert.DoesNotContain(FakeMicrosoftTodo.RefreshToken, storedJob.ToJson());
        Assert.DoesNotContain(FakeMicrosoftTodo.DeviceCode, storedJob.ToJson());
    }

    [Fact]
    public async Task Import_MapsTaskFieldsAndChecklistItems()
    {
        var fake = new FakeMicrosoftTodo();
        var list = fake.AddList("AAMkADIy=", "Errands");
        var detailedTask = new FakeTodoTask("detailed", new JsonObject
        {
            ["title"] = "  Plan the trip  ",
            ["importance"] = "high",
            ["status"] = "inProgress",
            ["createdDateTime"] = "2031-02-01T08:30:00.1234567Z",
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<html><head><style>p { margin: 0; }</style></head><body><p>Line one</p><p>Bread &amp; butter<br>Line three</p></body></html>" },
            ["dueDateTime"] = new JsonObject { ["dateTime"] = "2031-03-20T00:00:00.0000000", ["timeZone"] = "UTC" },
            ["isReminderOn"] = true,
            ["reminderDateTime"] = new JsonObject { ["dateTime"] = "2031-03-12T09:00:00.0000000", ["timeZone"] = "Pacific Standard Time" },
            ["recurrence"] = new JsonObject { ["pattern"] = new JsonObject { ["type"] = "weekly", ["interval"] = 2, ["daysOfWeek"] = new JsonArray("thursday", "monday"), ["firstDayOfWeek"] = "sunday" }, ["range"] = new JsonObject { ["type"] = "noEnd", ["startDate"] = "2031-03-20" } },
        });
        detailedTask.ChecklistItems.Add(new JsonObject { ["id"] = "c2", ["displayName"] = "Book the hotel", ["isChecked"] = true, ["createdDateTime"] = "2031-02-02T00:00:00Z" });
        detailedTask.ChecklistItems.Add(new JsonObject { ["id"] = "c1", ["displayName"] = "Pick dates", ["isChecked"] = false, ["createdDateTime"] = "2031-02-01T00:00:00Z" });
        detailedTask.ChecklistItems.Add(new JsonObject { ["id"] = "c3", ["displayName"] = "   ", ["isChecked"] = false, ["createdDateTime"] = "2031-02-03T00:00:00Z" });
        list.Tasks.Add(detailedTask);
        list.Tasks.Add(new FakeTodoTask("completed", new JsonObject
        {
            ["title"] = "Pay rent",
            ["importance"] = "normal",
            ["status"] = "completed",
            ["completedDateTime"] = new JsonObject { ["dateTime"] = "2031-03-05T10:30:00.0000000", ["timeZone"] = "UTC" },
            ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = "Transfer\r\nby Friday" },
            ["isReminderOn"] = true,
            ["reminderDateTime"] = new JsonObject { ["dateTime"] = "2031-03-20T09:00:00.0000000", ["timeZone"] = "UTC" },
            ["recurrence"] = new JsonObject { ["pattern"] = new JsonObject { ["type"] = "absoluteMonthly", ["interval"] = 1, ["dayOfMonth"] = 1 } },
        }));
        list.Tasks.Add(new FakeTodoTask("timed-due", new JsonObject
        {
            ["title"] = "Call the bank",
            ["status"] = "notStarted",
            ["dueDateTime"] = new JsonObject { ["dateTime"] = "2031-03-14T15:00:00.0000000", ["timeZone"] = "UTC" },
            ["isReminderOn"] = true,
            ["reminderDateTime"] = new JsonObject { ["dateTime"] = "2031-03-01T08:00:00.0000000", ["timeZone"] = "UTC" },
            ["recurrence"] = new JsonObject { ["pattern"] = new JsonObject { ["type"] = "relativeMonthly", ["interval"] = 1, ["daysOfWeek"] = new JsonArray("monday"), ["index"] = "first" } },
        }));
        list.Tasks.Add(new FakeTodoTask("untitled", new JsonObject { ["title"] = " ", ["status"] = "notStarted", ["isReminderOn"] = false }));
        list.Tasks.Add(new FakeTodoTask("daily", new JsonObject { ["title"] = "Water the plants", ["status"] = "notStarted", ["recurrence"] = new JsonObject { ["pattern"] = new JsonObject { ["type"] = "daily", ["interval"] = 3 } } }));
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");

        var job = await RunImportAsync(server, user);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.ListsImported);
        Assert.Equal(5, job.TasksImported);
        Assert.Equal(2, job.StepsImported);
        var importedList = Assert.Single(await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists"), candidate => !candidate.IsDefault);
        Assert.Equal("Errands", importedList.Name);
        var tasks = await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={importedList.Id}&includeCompleted=true");
        Assert.Equal(5, tasks.Count);

        var detailed = Assert.Single(tasks, task => task.Title == "Plan the trip");
        Assert.Equal("Line one\nBread & butter\nLine three", detailed.Body);
        Assert.True(detailed.IsImportant);
        Assert.False(detailed.IsCompleted);
        Assert.Equal(new DateOnly(2031, 3, 20), detailed.DueDate);
        Assert.Equal(new DateTimeOffset(2031, 3, 12, 16, 0, 0, TimeSpan.Zero), detailed.ReminderAt);
        Assert.Equal(RecurrenceFrequency.Weekly, detailed.Recurrence?.Frequency);
        Assert.Equal(2, detailed.Recurrence?.Interval);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], detailed.Recurrence?.DaysOfWeek ?? []);
        Assert.Equal(new DateTimeOffset(2031, 2, 1, 8, 30, 0, 123, TimeSpan.Zero), detailed.CreatedAt);
        Assert.Collection(detailed.Steps, step => Assert.Equal(("Pick dates", false), (step.Title, step.IsCompleted)), step => Assert.Equal(("Book the hotel", true), (step.Title, step.IsCompleted)));

        var completed = Assert.Single(tasks, task => task.Title == "Pay rent");
        Assert.True(completed.IsCompleted);
        Assert.Equal(new DateTimeOffset(2031, 3, 5, 10, 30, 0, TimeSpan.Zero), completed.CompletedAt);
        Assert.Equal("Transfer\nby Friday", completed.Body);
        Assert.Null(completed.ReminderAt);
        Assert.Null(completed.Recurrence);

        var timedDue = Assert.Single(tasks, task => task.Title == "Call the bank");
        Assert.Equal(new DateOnly(2031, 3, 15), timedDue.DueDate);
        Assert.Null(timedDue.Recurrence);
        Assert.Equal(new DateTimeOffset(2031, 3, 1, 8, 0, 0, TimeSpan.Zero), timedDue.ReminderAt);
        var storedTimedDue = await server.FindRawAsync(server.Database.Tasks, timedDue.Id);
        Assert.NotNull(storedTimedDue.ReminderSentAt);
        var storedDetailed = await server.FindRawAsync(server.Database.Tasks, detailed.Id);
        Assert.Null(storedDetailed.ReminderSentAt);
        Assert.Equal(storedDetailed.OwnerUserId, storedDetailed.ReminderSetByUserId);
        Assert.Equal((ImportSources.MicrosoftTodo, "detailed"), (storedDetailed.ExternalSource, storedDetailed.ExternalId));

        Assert.Single(tasks, task => task.Title == MicrosoftTodoTaskMapper.UntitledTaskTitle);
        var daily = Assert.Single(tasks, task => task.Title == "Water the plants");
        Assert.Equal(RecurrenceFrequency.Daily, daily.Recurrence?.Frequency);
        Assert.Equal(3, daily.Recurrence?.Interval);
        Assert.Equal(new DateOnly(2031, 3, 10), daily.DueDate);

        Assert.Contains(fake.GraphRequests, request => request.Uri.AbsolutePath == "/v1.0/me/todo/lists/AAMkADIy%3D/tasks/detailed/checklistItems");
    }

    [Fact]
    public async Task Import_FollowsNextLinksForListsTasksAndChecklistItems()
    {
        var fake = new FakeMicrosoftTodo { PageSize = 2 };
        for (var listNumber = 1; listNumber <= 3; listNumber++)
        {
            var list = fake.AddList($"list-{listNumber}", $"List {listNumber}");
            for (var taskNumber = 1; taskNumber <= 5; taskNumber++) list.Tasks.Add(FakeTodoTask.Create($"task-{listNumber}-{taskNumber}", $"Task {listNumber}.{taskNumber}"));
        }
        var taskWithSteps = fake.Lists[0].Tasks[0];
        for (var stepNumber = 1; stepNumber <= 5; stepNumber++) taskWithSteps.ChecklistItems.Add(new JsonObject { ["id"] = $"step-{stepNumber}", ["displayName"] = $"Step {stepNumber}", ["isChecked"] = false });
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");

        var job = await RunImportAsync(server, user);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.ListsImported);
        Assert.Equal(15, job.TasksImported);
        Assert.Equal(5, job.StepsImported);
        var lists = await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        Assert.Equal(["Tasks", "List 1", "List 2", "List 3"], lists.Select(list => list.Name));
        foreach (var list in lists.Where(list => !list.IsDefault))
        {
            var tasks = await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={list.Id}");
            Assert.Equal([.. Enumerable.Range(1, 5).Select(taskNumber => $"Task {list.Name[^1]}.{taskNumber}")], tasks.Select(task => task.Title));
        }
        var firstTask = Assert.Single(await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks"), task => task.Title == "Task 1.1");
        Assert.Equal([.. Enumerable.Range(1, 5).Select(stepNumber => $"Step {stepNumber}")], firstTask.Steps.Select(step => step.Title));
        Assert.Equal(3, fake.GraphRequests.Count(request => request.Uri.AbsolutePath == "/v1.0/me/todo/lists/list-1/tasks/task-1-1/checklistItems"));
        Assert.Equal(2, fake.GraphRequests.Count(request => request.Uri.AbsolutePath == "/v1.0/me/todo/lists"));
        Assert.Contains(fake.GraphRequests, request => request.Uri.Query == "?$skiptoken=4" && request.Uri.AbsolutePath == "/v1.0/me/todo/lists/list-2/tasks");
    }

    [Fact]
    public async Task Import_MergesTheDefaultListIntoTheUsersDefaultListAndLeavesOutFlaggedEmail()
    {
        var fake = new FakeMicrosoftTodo();
        fake.AddList("flagged", "Flagged email", "flaggedEmails").Tasks.Add(FakeTodoTask.Create("flagged-task", "Reply to Kim"));
        var defaultList = fake.AddList("todo-default", "Tasks", "defaultList");
        defaultList.Tasks.Add(FakeTodoTask.Create("default-1", "Imported first"));
        defaultList.Tasks.Add(FakeTodoTask.Create("default-2", "Imported second"));
        fake.AddList("groceries", "Groceries").Tasks.Add(FakeTodoTask.Create("groceries-1", "Eggs"));
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");
        var errandeskDefaultList = await server.GetDefaultListAsync(user);
        await user.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Existing task", ListId: errandeskDefaultList.Id), HttpStatusCode.Created);

        var job = await RunImportAsync(server, user);

        Assert.Equal(1, job.ListsImported);
        Assert.Equal(3, job.TasksImported);
        var lists = await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        Assert.Equal(["Tasks", "Groceries"], lists.Select(list => list.Name));
        Assert.Equal(errandeskDefaultList.Id, Assert.Single(lists, list => list.IsDefault).Id);
        var defaultTasks = await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={errandeskDefaultList.Id}");
        Assert.Equal(["Imported first", "Imported second", "Existing task"], defaultTasks.Select(task => task.Title));
        Assert.DoesNotContain(await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks"), task => task.Title == "Reply to Kim");
        Assert.DoesNotContain(fake.GraphRequests, request => request.Uri.AbsolutePath.Contains("/flagged/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_SecondRun_SkipsListsAndTasksImportedBefore()
    {
        var fake = new FakeMicrosoftTodo();
        fake.AddList("todo-default", "Tasks", "defaultList").Tasks.Add(FakeTodoTask.Create("default-1", "Water the plants"));
        var work = fake.AddList("work", "Work");
        work.Tasks.Add(FakeTodoTask.Create("work-1", "Write the report"));
        work.Tasks.Add(FakeTodoTask.Create("work-2", "Book a room"));
        var archive = fake.AddList("archive", "Archive");
        archive.Tasks.Add(FakeTodoTask.Create("archive-1", "Old task"));
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");
        var firstJob = await RunImportAsync(server, user);
        Assert.Equal((2, 4), (firstJob.ListsImported, firstJob.TasksImported));

        var allTasks = await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks");
        await user.Client.DeleteAsync($"/api/tasks/{Assert.Single(allTasks, task => task.Title == "Book a room").Id}");
        var importedArchive = Assert.Single(await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists"), list => list.Name == "Archive");
        await user.Client.DeleteAsync($"/api/lists/{importedArchive.Id}");
        work.Tasks.Add(FakeTodoTask.Create("work-3", "Send the invoice"));
        archive.Tasks.Add(FakeTodoTask.Create("archive-2", "Another old task"));
        fake.AddList("home", "Home").Tasks.Add(FakeTodoTask.Create("home-1", "Fix the door"));

        var secondJob = await RunImportAsync(server, user);

        Assert.Equal(ImportJobStatus.Completed, secondJob.Status);
        Assert.Equal(1, secondJob.ListsImported);
        Assert.Equal(2, secondJob.ListsSkipped);
        Assert.Equal(2, secondJob.TasksImported);
        Assert.Equal(3, secondJob.TasksSkipped);
        var lists = await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        Assert.Equal(["Tasks", "Work", "Home"], lists.Select(list => list.Name));
        var titles = (await user.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks")).Select(task => task.Title).Order().ToList();
        Assert.Equal(["Fix the door", "Send the invoice", "Water the plants", "Write the report"], titles);
        Assert.Equal(1, fake.GraphRequests.Count(request => request.Uri.AbsolutePath == "/v1.0/me/todo/lists/archive/tasks"));
        Assert.Equal(1, fake.GraphRequests.Count(request => request.Uri.AbsolutePath == "/v1.0/me/todo/lists/work/tasks/work-1/checklistItems"));
    }

    [Fact]
    public async Task Import_RefreshesTheAccessTokenWhenGraphRejectsIt()
    {
        var fake = new FakeMicrosoftTodo { ExpireFirstAccessTokenAfterRequests = 2 };
        var list = fake.AddList("work", "Work");
        for (var taskNumber = 1; taskNumber <= 3; taskNumber++) list.Tasks.Add(FakeTodoTask.Create($"work-{taskNumber}", $"Task {taskNumber}"));
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");

        var job = await RunImportAsync(server, user);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.TasksImported);
        var refreshRequest = Assert.Single(fake.IdentityRequests, request => HttpUtility.ParseQueryString(request.Body)["grant_type"] == "refresh_token");
        Assert.Equal(FakeMicrosoftTodo.RefreshToken, HttpUtility.ParseQueryString(refreshRequest.Body)["refresh_token"]);
        Assert.Equal($"Bearer {FakeMicrosoftTodo.RefreshedAccessToken}", fake.GraphRequests[^1].Headers["Authorization"]);
    }

    [Theory]
    [InlineData("authorization_declined", ImportJobStatus.Declined)]
    [InlineData("expired_token", ImportJobStatus.Expired)]
    public async Task Start_WhenTheSignInEndsWithoutTokens_ReportsWhyAndImportsNothing(string tokenError, ImportJobStatus expectedStatus)
    {
        var fake = new FakeMicrosoftTodo { TokenError = tokenError };
        fake.AddList("work", "Work").Tasks.Add(FakeTodoTask.Create("work-1", "Task"));
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");

        var job = await RunImportAsync(server, user);

        Assert.Equal(expectedStatus, job.Status);
        Assert.Equal(0, job.TasksImported);
        Assert.Empty(fake.GraphRequests);
        Assert.Single(await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists"));
    }

    [Fact]
    public async Task Start_WhileAnImportWaitsForSignIn_CancelsTheEarlierImport()
    {
        var fake = new FakeMicrosoftTodo { PendingPolls = int.MaxValue };
        await using var server = await StartServerAsync(fake);
        var user = await server.RegisterAsync("alice");
        var first = await user.Client.SendAsync<ImportJobResponse>(HttpMethod.Post, StartPath, null, HttpStatusCode.Created);

        var second = await user.Client.SendAsync<ImportJobResponse>(HttpMethod.Post, StartPath, null, HttpStatusCode.Created);

        await server.Services.GetRequiredService<MicrosoftTodoImportService>().WaitForRunAsync(first.Id).WaitAsync(s_runTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(ImportJobStatus.Cancelled, (await user.Client.GetAsync<ImportJobResponse>($"/api/import/jobs/{first.Id}")).Status);
        Assert.Equal(ImportJobStatus.WaitingForSignIn, (await user.Client.GetAsync<ImportJobResponse>($"/api/import/jobs/{second.Id}")).Status);
    }

    [Fact]
    public async Task GetJob_OfAnotherUserOrAnUnknownJob_Returns404()
    {
        var fake = new FakeMicrosoftTodo();
        await using var server = await StartServerAsync(fake, registrationMode: "open");
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob");
        var job = await RunImportAsync(server, alice);

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/import/jobs/{job.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/import/jobs/{Guid.CreateVersion7()}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetJob_LeftUnfinishedByAnEarlierProcess_ReportsItAsFailed()
    {
        await using var server = await StartServerAsync(new FakeMicrosoftTodo());
        var user = await server.RegisterAsync("alice");
        var orphanedJob = new ImportJobDocument { Id = Guid.CreateVersion7(), Source = ImportSources.MicrosoftTodo, Status = ImportJobStatus.Importing, TasksImported = 4 };
        orphanedJob.StampCreated(user.Id, server.TimeProvider.GetUtcNow().UtcDateTime, 1);
        await server.Database.ImportJobs.InsertOneAsync(orphanedJob, cancellationToken: TestContext.Current.CancellationToken);

        var job = await user.Client.GetAsync<ImportJobResponse>($"/api/import/jobs/{orphanedJob.Id}");

        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(MicrosoftTodoImportService.InterruptedMessage, job.Error);
        Assert.Equal(4, job.TasksImported);
    }

    private Task<ApiTestServer> StartServerAsync(FakeMicrosoftTodo fake, IReadOnlyDictionary<string, string>? additionalVariables = null, string registrationMode = "invite")
    {
        var variables = new Dictionary<string, string> { [ServerEnvironmentVariables.MicrosoftGraphClientId] = ClientId };
        foreach (var variable in additionalVariables ?? new Dictionary<string, string>()) variables[variable.Key] = variable.Value;
        return ApiTestServer.StartAsync(mongoDb, registrationMode, additionalVariables: variables, configureServices: services =>
        {
            services.AddSingleton<IHttpClientFactory>(new HandlerHttpClientFactory(fake.Handler));
            services.AddSingleton(new MicrosoftTodoImportOptions { PollInterval = TimeSpan.FromMilliseconds(10), MaximumRetryDelay = TimeSpan.FromMilliseconds(10) });
        });
    }

    private static async Task<ImportJobResponse> RunImportAsync(ApiTestServer server, TestUser user)
    {
        var started = await user.Client.SendAsync<ImportJobResponse>(HttpMethod.Post, StartPath, null, HttpStatusCode.Created);
        return await WaitForJobAsync(server, user, started.Id);
    }

    private static async Task<ImportJobResponse> WaitForJobAsync(ApiTestServer server, TestUser user, Guid jobId)
    {
        await server.Services.GetRequiredService<MicrosoftTodoImportService>().WaitForRunAsync(jobId).WaitAsync(s_runTimeout, TestContext.Current.CancellationToken);
        return await user.Client.GetAsync<ImportJobResponse>($"/api/import/jobs/{jobId}");
    }
}
