using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Serialization;

public sealed class OptionalSerializationTests
{
    [Fact]
    public void Serialize_WritesOnlySpecifiedValuesIncludingNull()
    {
        var json = ApiClient.SerializeJson(new UpdateTaskRequest { Title = "Call", DueDate = null, ExpectedChangeVersion = 7 });

        Assert.Equal("""{"title":"Call","dueDate":null,"expectedChangeVersion":7}""", json);
        Assert.Equal("{}", ApiClient.SerializeJson(new UpdateTaskRequest()));
    }

    [Fact]
    public void Deserialize_DistinguishesAbsentNullAndValues()
    {
        var request = ApiClient.DeserializeJson<UpdateTaskRequest>("""{"body":null,"dueDate":"2031-03-14","isCompleted":false,"listId":"0195f0b4-8b5e-7c3a-9d2e-4f6a1b2c3d4e"}""");

        Assert.False(request.Title.IsSpecified);
        Assert.True(request.Body.IsSpecified);
        Assert.Null(request.Body.Value);
        Assert.Equal(new DateOnly(2031, 3, 14), request.DueDate.Value);
        Assert.True(request.IsCompleted.IsSpecified);
        Assert.False(request.IsCompleted.Value);
        Assert.Equal(Guid.Parse("0195f0b4-8b5e-7c3a-9d2e-4f6a1b2c3d4e"), request.ListId.Value);
        Assert.False(request.SortOrder.IsSpecified);
        Assert.Null(request.ExpectedChangeVersion);
    }

    [Fact]
    public void NoteTags_RoundTripAsAbsentNullOrList()
    {
        var request = ApiClient.DeserializeJson<UpdateNoteRequest>("""{"tags":["work","ideas"],"title":null}""");

        Assert.Equal(["work", "ideas"], request.Tags.Value!);
        Assert.True(request.Title.IsSpecified);
        Assert.False(request.Content.IsSpecified);
        Assert.False(ApiClient.DeserializeJson<UpdateNoteRequest>("{}").Tags.IsSpecified);
        Assert.Null(ApiClient.DeserializeJson<UpdateNoteRequest>("""{"tags":null}""").Tags.Value);
        Assert.Equal("""{"tags":null,"isPinned":true}""", ApiClient.SerializeJson(new UpdateNoteRequest { Tags = new Optional<IReadOnlyList<string>?>(null), IsPinned = true }));
    }

    [Fact]
    public void Deserialize_NullForANonNullableValue_Throws() => Assert.Throws<JsonException>(() => ApiClient.DeserializeJson<UpdateTaskRequest>("""{"isCompleted":null}"""));
}
