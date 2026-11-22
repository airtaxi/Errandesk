using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Controls who may create an account on the server once the first user exists.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegistrationMode>))]
public enum RegistrationMode
{
    /// <summary>Anyone who can reach the server may register.</summary>
    [JsonStringEnumMemberName("open")]
    Open,

    /// <summary>Registration requires an invite code created by an administrator.</summary>
    [JsonStringEnumMemberName("invite")]
    Invite,

    /// <summary>Registration is closed.</summary>
    [JsonStringEnumMemberName("disabled")]
    Disabled,
}
