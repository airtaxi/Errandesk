using Errandesk.Server.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Errandesk.Server.Tests.Data;

public sealed class MongoDbSerializationTests
{
    public MongoDbSerializationTests() => MongoDbSerialization.Register();

    [Fact]
    public void Register_StoresGuidsAsStandardUuid()
    {
        var identifier = Guid.CreateVersion7();

        var document = new SampleDocument { Id = identifier }.ToBsonDocument();

        var binaryData = document["_id"].AsBsonBinaryData;
        Assert.Equal(BsonBinarySubType.UuidStandard, binaryData.SubType);
        Assert.Equal(identifier, binaryData.ToGuid());
    }

    [Fact]
    public void Register_StoresDateOnlyAsIsoString()
    {
        var document = new SampleDocument { DueDate = new DateOnly(2026, 3, 9) }.ToBsonDocument();

        Assert.Equal("2026-03-09", document["dueDate"].AsString);
        Assert.Equal(new DateOnly(2026, 3, 9), BsonSerializer.Deserialize<SampleDocument>(document).DueDate);
    }

    [Fact]
    public void Register_UsesCamelCaseNamesAndStringEnums()
    {
        var document = new SampleDocument { Priority = SamplePriority.High }.ToBsonDocument();

        Assert.Equal("High", document["priority"].AsString);
    }

    [Fact]
    public void Register_IgnoresUnknownElements()
    {
        var document = new SampleDocument().ToBsonDocument();
        document["futureField"] = 1;

        var exception = Record.Exception(() => BsonSerializer.Deserialize<SampleDocument>(document));

        Assert.Null(exception);
    }

    [Fact]
    public void Register_CanRunMoreThanOnce() => MongoDbSerialization.Register();
}
