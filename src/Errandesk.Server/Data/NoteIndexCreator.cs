using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>notes</c> collection, including the text index over title, content and tags.
/// </summary>
public sealed class NoteIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var notes = database.GetCollection<NoteDocument>(MongoCollectionNames.Notes);
        var noteKeys = Builders<NoteDocument>.IndexKeys;
        var textIndexOptions = new CreateIndexOptions<NoteDocument>
        {
            Name = MongoIndexNames.NoteText,
            DefaultLanguage = "none",
            Weights = new BsonDocument { ["title"] = 10, ["tags"] = 5, ["content"] = 1 },
        };
        var textIndexKeys = noteKeys.Ascending(note => note.OwnerUserId).Text(note => note.Title).Text(note => note.Tags).Text(note => note.Content);

        await notes.Indexes.CreateManyAsync([new CreateIndexModel<NoteDocument>(noteKeys.Ascending(note => note.OwnerUserId).Ascending(note => note.IsDeleted).Descending(note => note.UpdatedAt)), new CreateIndexModel<NoteDocument>(noteKeys.Ascending(note => note.OwnerUserId).Ascending(note => note.Tags)), new CreateIndexModel<NoteDocument>(noteKeys.Ascending(note => note.OwnerUserId).Ascending(note => note.LinkedTaskIds)), new CreateIndexModel<NoteDocument>(noteKeys.Ascending(note => note.OwnerUserId).Ascending(note => note.ChangeVersion)), new CreateIndexModel<NoteDocument>(textIndexKeys, textIndexOptions)], cancellationToken);
    }
}
