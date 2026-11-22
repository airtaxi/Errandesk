using System.Text.Json.Serialization;

namespace Errandesk.Server.Import;

/// <summary>
/// One page of a Microsoft Graph collection, with the address of the next page when there is one.
/// </summary>
public sealed record GraphCollectionPage<TItem>(IReadOnlyList<TItem>? Value, [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
