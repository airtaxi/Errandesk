using System.Text.Json.Serialization;
using Errandesk.Client.Models;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Source-generated JSON metadata for data the client stores on the device.
/// </summary>
[JsonSerializable(typeof(List<ReminderNotification>))]
[JsonSerializable(typeof(SignedInUserRecord))]
public sealed partial class ClientJsonSerializerContext : JsonSerializerContext;
