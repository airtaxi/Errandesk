namespace Errandesk.Server.Import;

/// <summary>
/// What an import created and skipped so far.
/// </summary>
public sealed class ImportCounts
{
    public int ListsImported { get; set; }

    public int ListsSkipped { get; set; }

    public int TasksImported { get; set; }

    public int TasksSkipped { get; set; }

    public int StepsImported { get; set; }
}
