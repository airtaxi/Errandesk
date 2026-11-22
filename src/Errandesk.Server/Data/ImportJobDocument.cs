using Errandesk.Contracts;

namespace Errandesk.Server.Data;

/// <summary>
/// An import of a user's data from another service. Sign-in tokens are never stored.
/// </summary>
public sealed class ImportJobDocument : DocumentBase
{
    /// <summary>One of <see cref="ImportSources"/>.</summary>
    public string Source { get; set; } = "";

    public ImportJobStatus Status { get; set; }

    public string? UserCode { get; set; }

    public string? VerificationUri { get; set; }

    /// <summary>When the sign-in code expires, in UTC.</summary>
    public DateTime? ExpiresAt { get; set; }

    public int ListsImported { get; set; }

    public int ListsSkipped { get; set; }

    public int TasksImported { get; set; }

    public int TasksSkipped { get; set; }

    public int StepsImported { get; set; }

    public string? Error { get; set; }

    public DateTime? CompletedAt { get; set; }
}
