using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// A request value that is either absent or specified, possibly as <see langword="null"/>.
/// PATCH requests use it so that an absent property leaves the stored value unchanged while an explicit <see langword="null"/> clears it.
/// </summary>
/// <remarks>
/// Declare properties of this type with <c>[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]</c> so unspecified values are not written.
/// </remarks>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    public Optional(T value)
    {
        Value = value;
        IsSpecified = true;
    }

    /// <summary>The specified value, or the default of <typeparamref name="T"/> when the value is absent.</summary>
    public T Value { get; }

    /// <summary>Whether the value was present in the request.</summary>
    public bool IsSpecified { get; }

    public static implicit operator Optional<T>(T value) => new(value);
}
