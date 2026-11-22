using System.Text.RegularExpressions;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Creates, searches, changes and deletes the notes of a user, links notes to tasks the user can see, and counts tags. Notes stay private, also when they are linked to tasks in shared lists.
/// </summary>
public sealed class NoteService(ErrandeskDatabase database, TaskListAccess taskListAccess, DocumentUpdater documentUpdater)
{
    private const string ResourceName = "note";
    private const string TaskResourceName = "task";
    private const string TextScoreFieldName = "textScore";
    private const int MaximumSearchTermCount = 10;

    /// <summary>
    /// The user's notes, pinned notes first. Without <paramref name="query"/> the rest is ordered by the most recent change; with it, notes match when the text index matches any word
    /// or when every word appears in the title, the content or a tag, and better text matches come first.
    /// </summary>
    public async Task<IReadOnlyList<NoteResponse>> QueryAsync(Guid userId, string? query, string? tag, Guid? linkedTaskId, CancellationToken cancellationToken)
    {
        var searchText = InputValidation.SearchQuery(query);
        var filterBuilder = Builders<NoteDocument>.Filter;
        var filter = VisibleNotesFilter(userId);
        if (!string.IsNullOrWhiteSpace(tag)) filter &= filterBuilder.AnyEq(note => note.Tags, InputValidation.Tag(tag));
        if (linkedTaskId is { } taskId)
        {
            await EnsureTaskVisibleAsync(userId, taskId, cancellationToken);
            filter &= filterBuilder.AnyEq(note => note.LinkedTaskIds, taskId);
        }

        Dictionary<Guid, double> textScores = [];
        if (searchText is not null)
        {
            textScores = await FindTextScoresAsync(filter, searchText, cancellationToken);
            filter &= filterBuilder.In(note => note.Id, textScores.Keys) | CreateSubstringFilter(searchText);
        }

        var notes = await database.Notes.Find(filter).ToListAsync(cancellationToken);
        var visibleTaskIds = await GetVisibleTaskIdsAsync(userId, notes.SelectMany(note => note.LinkedTaskIds), cancellationToken);
        return [.. notes.OrderByDescending(note => note.IsPinned).ThenByDescending(note => textScores.GetValueOrDefault(note.Id)).ThenByDescending(note => note.UpdatedAt).ThenByDescending(note => note.Id).Select(note => note.ToResponse(visibleTaskIds))];
    }

    /// <summary>Summaries of the notes changed at or after <paramref name="since"/>, most recently changed first, at most <paramref name="limit"/>.</summary>
    public async Task<IReadOnlyList<NoteSummaryResponse>> GetRecentlyChangedAsync(Guid userId, DateTime since, int limit, CancellationToken cancellationToken)
    {
        var filter = VisibleNotesFilter(userId) & Builders<NoteDocument>.Filter.Gte(note => note.UpdatedAt, since);
        var notes = await database.Notes.Find(filter).SortByDescending(note => note.UpdatedAt).ThenByDescending(note => note.Id).Limit(limit).ToListAsync(cancellationToken);
        return [.. notes.Select(note => note.ToSummaryResponse())];
    }

    public async Task<NoteResponse> GetAsync(Guid userId, Guid noteId, CancellationToken cancellationToken) => await ToResponseAsync(userId, await GetVisibleAsync(userId, noteId, cancellationToken), cancellationToken);

    public async Task<CreateResult<NoteResponse>> CreateAsync(Guid userId, CreateNoteRequest request, CancellationToken cancellationToken)
    {
        var title = InputValidation.OptionalText(request.Title?.Trim(), "title", InputValidation.MaximumNoteTitleLength);
        var content = InputValidation.OptionalText(request.Content, "content", InputValidation.MaximumNoteContentLength);
        var tags = InputValidation.Tags(request.Tags);
        var noteId = InputValidation.ClientId(request.Id);
        var linkedTaskIds = (request.LinkedTaskIds ?? []).Distinct().ToList();
        if (linkedTaskIds.Count > InputValidation.MaximumLinkedTaskCount) throw new RequestValidationException($"A note can be linked to at most {InputValidation.MaximumLinkedTaskCount} tasks.");

        var visibleTaskIds = await GetVisibleTaskIdsAsync(userId, linkedTaskIds, cancellationToken);
        if (linkedTaskIds.Any(taskId => !visibleTaskIds.Contains(taskId))) throw new ResourceNotFoundException(TaskResourceName);

        var note = new NoteDocument { Id = noteId, Title = title, Content = content, Tags = tags, LinkedTaskIds = linkedTaskIds, IsPinned = request.IsPinned };
        var result = await documentUpdater.InsertIdempotentAsync(database.Notes, note, userId, ResourceName, cancellationToken);
        return new CreateResult<NoteResponse>(await ToResponseAsync(userId, result.Resource, cancellationToken), result.IsCreated);
    }

    public async Task<NoteResponse> UpdateAsync(Guid userId, Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title.IsSpecified ? InputValidation.OptionalText(request.Title.Value?.Trim(), "title", InputValidation.MaximumNoteTitleLength) : null;
        var content = request.Content.IsSpecified ? InputValidation.OptionalText(request.Content.Value, "content", InputValidation.MaximumNoteContentLength) : null;
        var tags = request.Tags.IsSpecified ? InputValidation.Tags(request.Tags.Value) : null;

        var note = await UpdateNoteAsync(userId, noteId, request.ExpectedChangeVersion, current =>
        {
            if (title is not null) current.Title = title;
            if (content is not null) current.Content = content;
            if (tags is not null) current.Tags = tags;
            if (request.IsPinned.IsSpecified) current.IsPinned = request.IsPinned.Value;
        }, cancellationToken);
        return await ToResponseAsync(userId, note, cancellationToken);
    }

    public Task DeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken) => documentUpdater.SoftDeleteAsync(database.Notes, VisibleFilter(userId, noteId), ResourceName, cancellationToken);

    /// <summary>Links the note to a task of the user. Linking an already linked task returns the note unchanged.</summary>
    public async Task<NoteResponse> LinkTaskAsync(Guid userId, Guid noteId, Guid taskId, CancellationToken cancellationToken)
    {
        var existingNote = await GetVisibleAsync(userId, noteId, cancellationToken);
        await EnsureTaskVisibleAsync(userId, taskId, cancellationToken);
        if (existingNote.LinkedTaskIds.Contains(taskId)) return await ToResponseAsync(userId, existingNote, cancellationToken);

        var note = await UpdateNoteAsync(userId, noteId, null, current =>
        {
            if (current.LinkedTaskIds.Contains(taskId)) return;
            if (current.LinkedTaskIds.Count >= InputValidation.MaximumLinkedTaskCount) throw new RequestValidationException($"A note can be linked to at most {InputValidation.MaximumLinkedTaskCount} tasks.");
            current.LinkedTaskIds.Add(taskId);
        }, cancellationToken);
        return await ToResponseAsync(userId, note, cancellationToken);
    }

    /// <summary>Removes the link between the note and a task. Unlinking a task that is not linked returns the note unchanged.</summary>
    public async Task<NoteResponse> UnlinkTaskAsync(Guid userId, Guid noteId, Guid taskId, CancellationToken cancellationToken)
    {
        var existingNote = await GetVisibleAsync(userId, noteId, cancellationToken);
        if (!existingNote.LinkedTaskIds.Contains(taskId)) return await ToResponseAsync(userId, existingNote, cancellationToken);

        var note = await UpdateNoteAsync(userId, noteId, null, current => current.LinkedTaskIds.Remove(taskId), cancellationToken);
        return await ToResponseAsync(userId, note, cancellationToken);
    }

    /// <summary>Every tag of the user's notes with the number of notes that carry it, ordered by name.</summary>
    public async Task<IReadOnlyList<TagResponse>> ListTagsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var groups = await database.Notes.Aggregate().Match(VisibleNotesFilter(userId)).Unwind<NoteDocument, BsonDocument>(note => note.Tags).Group(new BsonDocument { ["_id"] = "$tags", ["noteCount"] = new BsonDocument("$sum", 1) }).ToListAsync(cancellationToken);
        return [.. groups.Select(group => new TagResponse(group["_id"].AsString, group["noteCount"].ToInt32())).OrderBy(tag => tag.Name, StringComparer.Ordinal)];
    }

    /// <summary>Summaries of the user's notes linked to each of <paramref name="taskIds"/>, pinned notes first, then the most recently changed. Tasks without linked notes are absent.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<NoteSummaryResponse>>> GetLinkedNoteSummariesAsync(Guid userId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<NoteSummaryResponse>>();

        var filter = VisibleNotesFilter(userId) & Builders<NoteDocument>.Filter.AnyIn(note => note.LinkedTaskIds, taskIds);
        var notes = await database.Notes.Find(filter).ToListAsync(cancellationToken);
        var requestedTaskIds = taskIds.ToHashSet();
        Dictionary<Guid, List<NoteSummaryResponse>> summariesByTaskId = [];
        foreach (var note in notes.OrderByDescending(note => note.IsPinned).ThenByDescending(note => note.UpdatedAt).ThenByDescending(note => note.Id))
        {
            var summary = note.ToSummaryResponse();
            foreach (var taskId in note.LinkedTaskIds.Where(requestedTaskIds.Contains))
            {
                if (!summariesByTaskId.TryGetValue(taskId, out var summaries)) summariesByTaskId[taskId] = summaries = [];
                summaries.Add(summary);
            }
        }
        return summariesByTaskId.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<NoteSummaryResponse>)entry.Value);
    }

    private async Task<NoteDocument> GetVisibleAsync(Guid userId, Guid noteId, CancellationToken cancellationToken) => await database.Notes.Find(VisibleFilter(userId, noteId)).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    private async Task<NoteDocument> UpdateNoteAsync(Guid userId, Guid noteId, long? expectedChangeVersion, Action<NoteDocument> applyChanges, CancellationToken cancellationToken)
    {
        try { return await documentUpdater.UpdateAsync(database.Notes, VisibleFilter(userId, noteId), ResourceName, expectedChangeVersion, current => current, applyChanges, cancellationToken); }
        catch (ChangeVersionConflictException exception) when (exception.CurrentResource is NoteDocument currentNote) { throw new ChangeVersionConflictException(await ToResponseAsync(userId, currentNote, cancellationToken)); }
    }

    private async Task<NoteResponse> ToResponseAsync(Guid userId, NoteDocument note, CancellationToken cancellationToken) => note.ToResponse(await GetVisibleTaskIdsAsync(userId, note.LinkedTaskIds, cancellationToken));

    private async Task EnsureTaskVisibleAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
    {
        var filter = await taskListAccess.CreateVisibleTaskFilterAsync(userId, taskId, cancellationToken);
        if (!await database.Tasks.Find(filter).AnyAsync(cancellationToken)) throw new ResourceNotFoundException(TaskResourceName);
    }

    private async Task<HashSet<Guid>> GetVisibleTaskIdsAsync(Guid userId, IEnumerable<Guid> taskIds, CancellationToken cancellationToken)
    {
        var distinctTaskIds = taskIds.Distinct().ToList();
        if (distinctTaskIds.Count == 0) return [];

        var filter = await taskListAccess.CreateVisibleTasksFilterAsync(userId, cancellationToken) & Builders<TaskDocument>.Filter.In(task => task.Id, distinctTaskIds);
        var visibleTaskIds = await database.Tasks.Find(filter).Project(task => task.Id).ToListAsync(cancellationToken);
        return [.. visibleTaskIds];
    }

    private async Task<Dictionary<Guid, double>> FindTextScoresAsync(FilterDefinition<NoteDocument> filter, string searchText, CancellationToken cancellationToken)
    {
        var textFilter = filter & Builders<NoteDocument>.Filter.Text(searchText);
        var projection = Builders<NoteDocument>.Projection.Include(note => note.Id).MetaTextScore(TextScoreFieldName);
        var matches = await database.Notes.Find(textFilter).Project<BsonDocument>(projection).ToListAsync(cancellationToken);
        return matches.ToDictionary(match => match["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard), match => match[TextScoreFieldName].ToDouble());
    }

    /// <summary>Matches notes in which every word of <paramref name="searchText"/> appears, ignoring case, in the title, the content or a tag.</summary>
    private static FilterDefinition<NoteDocument> CreateSubstringFilter(string searchText)
    {
        var filterBuilder = Builders<NoteDocument>.Filter;
        var terms = searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumSearchTermCount);
        return filterBuilder.And(terms.Select(term =>
        {
            var pattern = new BsonRegularExpression(Regex.Escape(term), "i");
            return filterBuilder.Regex(note => note.Title, pattern) | filterBuilder.Regex(note => note.Content, pattern) | filterBuilder.Regex(note => note.Tags, pattern);
        }));
    }

    private static FilterDefinition<NoteDocument> VisibleNotesFilter(Guid userId) => Builders<NoteDocument>.Filter.Eq(note => note.OwnerUserId, userId) & Builders<NoteDocument>.Filter.Eq(note => note.IsDeleted, false);

    private static FilterDefinition<NoteDocument> VisibleFilter(Guid userId, Guid noteId) => Builders<NoteDocument>.Filter.Where(note => note.Id == noteId && note.OwnerUserId == userId && !note.IsDeleted);
}
