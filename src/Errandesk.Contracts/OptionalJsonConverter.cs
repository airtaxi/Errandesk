using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Errandesk.Contracts;

/// <summary>
/// Reads a present JSON value, including <c>null</c>, as a specified <see cref="Optional{T}"/> and writes the contained value.
/// </summary>
public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override bool HandleNull => true;

    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(JsonSerializer.Deserialize(ref reader, GetTypeInfo(options))!);

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Value, GetTypeInfo(options));

    private static JsonTypeInfo<T> GetTypeInfo(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
