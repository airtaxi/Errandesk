namespace Errandesk.Contracts;

/// <summary>
/// An import of data from another service, with the sign-in code while it waits for the user and the counts of what it imported.
/// </summary>
/// <param name="Id">Job ID.</param>
/// <param name="Source">Where the data comes from, one of <see cref="ImportSources"/>.</param>
/// <param name="Status">Current state of the job.</param>
/// <param name="UserCode">Code the user enters at <paramref name="VerificationUri"/> to sign in.</param>
/// <param name="VerificationUri">Address of the sign-in page.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="ListsImported">Lists created by the import.</param>
/// <param name="ListsSkipped">Lists that an earlier import already created.</param>
/// <param name="TasksImported">Tasks created by the import.</param>
/// <param name="TasksSkipped">Tasks that an earlier import already created.</param>
/// <param name="StepsImported">Steps created inside the imported tasks.</param>
/// <param name="Error">Why the job failed, when <paramref name="Status"/> is <see cref="ImportJobStatus.Failed"/>.</param>
/// <param name="CreatedAt">When the job was started.</param>
/// <param name="CompletedAt">When the job reached a final state.</param>
public sealed record ImportJobResponse(Guid Id, string Source, ImportJobStatus Status, string? UserCode, string? VerificationUri, DateTimeOffset? ExpiresAt, int ListsImported, int ListsSkipped, int TasksImported, int TasksSkipped, int StepsImported, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
