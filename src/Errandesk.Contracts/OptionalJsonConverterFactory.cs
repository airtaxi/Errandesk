using System.Text.Json;
using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Creates <see cref="OptionalJsonConverter{T}"/> instances for a fixed set of value types, without runtime code generation.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    private static readonly Dictionary<Type, Func<JsonConverter>> s_converterFactories = new()
    {
        [typeof(string)] = () => new OptionalJsonConverter<string?>(),
        [typeof(bool)] = () => new OptionalJsonConverter<bool>(),
        [typeof(int)] = () => new OptionalJsonConverter<int>(),
        [typeof(long)] = () => new OptionalJsonConverter<long>(),
        [typeof(double)] = () => new OptionalJsonConverter<double>(),
        [typeof(Guid)] = () => new OptionalJsonConverter<Guid>(),
        [typeof(Guid?)] = () => new OptionalJsonConverter<Guid?>(),
        [typeof(DateOnly)] = () => new OptionalJsonConverter<DateOnly>(),
        [typeof(DateOnly?)] = () => new OptionalJsonConverter<DateOnly?>(),
        [typeof(DateTimeOffset)] = () => new OptionalJsonConverter<DateTimeOffset>(),
        [typeof(DateTimeOffset?)] = () => new OptionalJsonConverter<DateTimeOffset?>(),
        [typeof(IReadOnlyList<string>)] = () => new OptionalJsonConverter<IReadOnlyList<string>?>(),
        [typeof(TaskRecurrence)] = () => new OptionalJsonConverter<TaskRecurrence?>(),
    };

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return s_converterFactories.TryGetValue(valueType, out var createConverter) ? createConverter() : throw new NotSupportedException($"Optional values of type {valueType} are not supported.");
    }
}
