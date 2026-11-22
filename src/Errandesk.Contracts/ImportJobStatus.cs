using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// The state of an import job.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportJobStatus>))]
public enum ImportJobStatus
{
    /// <summary>The user has not finished signing in with the code yet.</summary>
    [JsonStringEnumMemberName("waitingForSignIn")]
    WaitingForSignIn,

    /// <summary>The user signed in and the data is being imported.</summary>
    [JsonStringEnumMemberName("importing")]
    Importing,

    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>The import stopped because of an error; the counts show what was imported before it.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>The code expired before the user signed in.</summary>
    [JsonStringEnumMemberName("expired")]
    Expired,

    /// <summary>The user declined the sign-in or the requested permissions.</summary>
    [JsonStringEnumMemberName("declined")]
    Declined,

    /// <summary>A newer import replaced this one before the user signed in.</summary>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}
