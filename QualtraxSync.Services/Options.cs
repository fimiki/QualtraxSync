namespace QualtraxSync.Services;

public class Options
{
    public const string Section = "Sync";
    public static Dictionary<string, string> Map => new()
    {
        { "--EarliestDocument", "Sync:EarliestDocument" },
        { "--IntervalSeconds", "Sync:IntervalSeconds" }
    };

    /// <summary>
    /// The date the first document was published in Qualtrax.  Whenever a new root folder is added, all documents in that folder will be synced from this date forward.
    /// </summary>
    public DateTimeOffset EarliestDocument { get; set; } = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// How frequently to check for new documents and revisions in Qualtrax, in seconds.  Default is 300 seconds (5 minutes).
    /// </summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to sync archived revisions of a document.  Set during app startup based on the SharePoint:Lifecycle:IncludeArchived configuration value.
    /// </summary>
    internal bool SyncArchived { get; set; }

    /// <summary>
    /// Weather to sync retired revisions of a document.  Set during app startup based on the SharePoint:Lifecycle:IncludeRetired configuration value.
    /// </summary>
    internal bool SyncRetired { get; set; }
}