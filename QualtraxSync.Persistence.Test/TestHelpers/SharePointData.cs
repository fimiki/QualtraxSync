namespace QualtraxSync.Persistence.Test.TestHelpers;

/// <summary>
/// The SharePoint content used by the tests.  The paths mirror exactly what the persistence handlers would have
/// written for this content, so they double as the expected paths for round trip assertions.
/// </summary>
public static class SharePointData
{
    public const int ManualsId = 1;
    public const int PoliciesId = 2;

    public const int QualityManualId = 10;
    public const int TravelPolicyId = 20;
    public const int OldFormId = 30;

    public const int QualityManualRevision1 = 1;
    public const int QualityManualRevision2 = 2;
    public const int TravelPolicyRevision = 3;
    public const int OldFormRevision = 4;

    public const string ManualsName = "Manuals";
    public const string PoliciesName = "Policies";
    public const string QualityManualName = "Quality Manual";
    public const string TravelPolicyName = "Travel Policy";
    public const string OldFormName = "Old Form";

    public static readonly DateTimeOffset ManualsCreated = Local(2023, 1, 1);
    public static readonly DateTimeOffset ManualsModified = Local(2024, 6, 1);
    public static readonly DateTimeOffset PoliciesCreated = Local(2023, 2, 1);
    public static readonly DateTimeOffset PoliciesModified = Local(2024, 5, 1);

    public static readonly DateTimeOffset QualityManualCreated = Local(2023, 1, 5);
    public static readonly DateTimeOffset QualityManualRevision1Published = Local(2023, 6, 1);
    public static readonly DateTimeOffset QualityManualRevision1Archived = Local(2024, 1, 1);
    public static readonly DateTimeOffset QualityManualRevision2Published = Local(2024, 1, 1);

    public static readonly DateTimeOffset TravelPolicyCreated = Local(2023, 3, 1);
    public static readonly DateTimeOffset TravelPolicyPublished = Local(2024, 3, 1);

    public static readonly DateTimeOffset OldFormCreated = Local(2021, 1, 1);
    public static readonly DateTimeOffset OldFormPublished = Local(2022, 1, 1);
    public static readonly DateTimeOffset OldFormArchived = Local(2023, 1, 1);

    // folder paths as they exist in SharePoint (the Qualtrax metadata is applied to each lifecycle copy of a folder)
    public const string ManualsCurrentPath = "Manuals/Current";
    public const string ManualsArchivedPath = "Manuals/Archived";
    public const string ManualsRetiredPath = "Manuals/Retired";
    public const string PoliciesCurrentPath = "Manuals/Current/Policies";

    // file paths as they exist in SharePoint
    public const string QualityManualRevision1Path = "Manuals/Archived/Quality Manual - 001 - 2023-06-01 through 2024-01-01.pdf";
    public const string QualityManualRevision2Path = "Manuals/Current/Quality Manual - 002 - 2024-01-01.pdf";
    public const string TravelPolicyPath = "Manuals/Current/Policies/Travel Policy - 003 - 2024-03-01.docx";
    public const string OldFormPath = "Manuals/Retired/Old Form - 004 - 2022-01-01 through 2023-01-01.pdf";

    public static void Seed(FakeMetadataService metadata)
    {
        var manuals = new Models.Folder { Id = ManualsId, Created = ManualsCreated, Modified = ManualsModified };

        metadata.SeedFolder(ManualsCurrentPath, manuals);
        metadata.SeedFolder(ManualsArchivedPath, manuals);
        metadata.SeedFolder(ManualsRetiredPath, manuals);
        metadata.SeedFolder(PoliciesCurrentPath, new Models.Folder { Id = PoliciesId, Created = PoliciesCreated, Modified = PoliciesModified });

        metadata.SeedFile(QualityManualRevision1Path, new Models.File
        {
            Title = QualityManualName,
            Id = QualityManualId,
            Revision = QualityManualRevision1,
            Published = QualityManualRevision1Published,
            Archived = QualityManualRevision1Archived,
            Created = QualityManualCreated
        });

        metadata.SeedFile(QualityManualRevision2Path, new Models.File
        {
            Title = QualityManualName,
            Id = QualityManualId,
            Revision = QualityManualRevision2,
            Published = QualityManualRevision2Published,
            Archived = null,
            Created = QualityManualCreated
        });

        metadata.SeedFile(TravelPolicyPath, new Models.File
        {
            Title = TravelPolicyName,
            Id = TravelPolicyId,
            Revision = TravelPolicyRevision,
            Published = TravelPolicyPublished,
            Archived = null,
            Created = TravelPolicyCreated
        });

        metadata.SeedFile(OldFormPath, new Models.File
        {
            Title = OldFormName,
            Id = OldFormId,
            Revision = OldFormRevision,
            Published = OldFormPublished,
            Archived = OldFormArchived,
            Created = OldFormCreated
        });
    }

    public static DateTimeOffset Local(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(year, month, day)));
}
