using System.Text.Json;
using System.Text.Json.Serialization;

namespace Errandesk.Server.Import;

/// <summary>
/// Source-generated JSON metadata for the Microsoft identity platform and Microsoft Graph responses the import reads.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DeviceCodeResponse))]
[JsonSerializable(typeof(TokenEndpointResponse))]
[JsonSerializable(typeof(GraphCollectionPage<GraphTodoTaskList>))]
[JsonSerializable(typeof(GraphCollectionPage<GraphTodoTask>))]
[JsonSerializable(typeof(GraphCollectionPage<GraphChecklistItem>))]
public sealed partial class MicrosoftGraphJsonSerializerContext : JsonSerializerContext;
