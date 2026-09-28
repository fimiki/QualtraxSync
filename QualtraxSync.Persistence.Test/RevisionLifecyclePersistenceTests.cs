using QualtraxSync.Persistence.Test.TestHelpers;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates that revision lifecycle changes (new revisions, archival, retirement and restoration) are persisted from
/// the paths the content was retrieved from to the paths dictated by the new lifecycle.
/// </summary>
public class RevisionLifecyclePersistenceTests
{
    private static readonly DateTimeOffset Published = SharePointData.Local(2024, 7, 1);
    private static readonly DateTimeOffset Retired = SharePointData.Local(2024, 9, 1);
    private static readonly DateTimeOffset Restored = SharePointData.Local(2024, 10, 1);

    [Fact]
    public async Task Revise_UploadsTheNewRevisionToTheCurrentLifecycle()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        const string expected = "Manuals/Current/Policies/Travel Policy - 005 - 2024-07-01.docx";

        Assert.Contains(expected, harness.Files.UploadedPaths);
        Assert.Equal("v5", harness.Files.Uploads.Single(u => u.Path == expected).Contents);
        Assert.Contains(harness.Metadata.Updates, u => u.Path == expected);
    }

    [Fact]
    public async Task Revise_ArchivesThePreviousRevisionFromThePathItWasRetrievedFrom()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        const string archived = "Manuals/Archived/Policies/Travel Policy - 003 - 2024-03-01 through 2024-07-01.docx";

        Assert.True(harness.Files.Moved(SharePointData.TravelPolicyPath, archived));

        var metadata = harness.Metadata.FileUpdateAt(archived);

        Assert.NotNull(metadata);
        Assert.Equal(SharePointData.TravelPolicyName, metadata.Title);
        Assert.Equal(SharePointData.TravelPolicyRevision, metadata.Revision);
        Assert.Equal(SharePointData.TravelPolicyPublished, metadata.Published);
        Assert.Equal(Published, metadata.Archived);
    }

    [Fact]
    public async Task Retire_MovesEveryRevisionIntoTheRetiredLifecycle()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Retire(Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision1Path, "Manuals/Retired/Quality Manual - 001 - 2023-06-01 through 2024-01-01.pdf"));
        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision2Path, "Manuals/Retired/Quality Manual - 002 - 2024-01-01 through 2024-09-01.pdf"));
    }

    [Fact]
    public async Task Retire_WritesTheArchivedDateToTheRetiredRevision()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Retire(Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        var metadata = harness.Metadata.FileUpdateAt("Manuals/Retired/Quality Manual - 002 - 2024-01-01 through 2024-09-01.pdf");

        Assert.NotNull(metadata);
        Assert.Equal(Retired, metadata.Archived);
        Assert.Equal(SharePointData.QualityManualRevision2Published, metadata.Published);
    }

    [Fact]
    public async Task Unretire_RestoresARetiredDocumentFromThePathItWasRetrievedFrom()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.OldFormId);

        document.Unretire(Restored);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        const string restored = "Manuals/Current/Old Form - 004 - 2022-01-01.pdf";

        Assert.True(harness.Files.Moved(SharePointData.OldFormPath, restored));

        var metadata = harness.Metadata.FileUpdateAt(restored);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Archived);
        Assert.Equal(SharePointData.OldFormPublished, metadata.Published);
        Assert.False(document.Retired);
    }

    [Fact]
    public async Task RetireThenUnretire_ReturnsEveryRevisionToItsOriginalLifecycle()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Retire(Retired);
        await harness.SaveAsync(TestContext.Current.CancellationToken);

        harness.Files.Reset();
        harness.Metadata.Reset();

        document.Unretire(Restored);
        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(
            "Manuals/Retired/Quality Manual - 002 - 2024-01-01 through 2024-09-01.pdf",
            SharePointData.QualityManualRevision2Path));

        Assert.True(harness.Files.Moved(
            "Manuals/Retired/Quality Manual - 001 - 2023-06-01 through 2024-01-01.pdf",
            SharePointData.QualityManualRevision1Path));

        var metadata = harness.Metadata.FileUpdateAt(SharePointData.QualityManualRevision2Path);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Archived);
    }

    [Fact]
    public async Task Retire_DeletesEveryRevisionWhenRetiredContentIsExcluded()
    {
        var harness = new PersistenceHarness(useLifecycleFolders: true, includeRetired: false);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        var subFolder = new Domain.Entities.Folder(99, "SubFolder", document.Folder, SharePointData.Local(2024, 1, 1), SharePointData.Local(2024, 1, 1));
        harness.Folders.Add(subFolder);

        document.Retire(Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Collection(harness.Files.DeletedPaths,
            path => Assert.Equal(SharePointData.QualityManualRevision1Path, path),
            path => Assert.Equal(SharePointData.QualityManualRevision2Path, path),
            path => Assert.Equal("Manuals/Archived", path));
        Assert.Null(await harness.Documents.GetAsync(SharePointData.QualityManualId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revise_WithoutLifecycleFolders_KeepsEveryRevisionInTheDocumentFolder()
    {
        var harness = new PersistenceHarness(useLifecycleFolders: false);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Manuals/Policies/Travel Policy - 005 - 2024-07-01.docx", harness.Files.UploadedPaths);
    }

    [Fact]
    public async Task Revise_DeletesEveryRevisionWhenRetiredContentIsExcludedAndDocumentBecomesRetired()
    {
        var harness = new PersistenceHarness(useLifecycleFolders: true, includeRetired: false);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        var subFolder = new Domain.Entities.Folder(99, "SubFolder", document.Folder, SharePointData.Local(2024, 1, 1), SharePointData.Local(2024, 1, 1));
        harness.Folders.Add(subFolder);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Collection(harness.Files.DeletedPaths,
            path => Assert.Equal(SharePointData.QualityManualRevision1Path, path),
            path => Assert.Equal(SharePointData.QualityManualRevision2Path, path),       
            path => Assert.Equal("Manuals/Archived", path));
        Assert.Null(await harness.Documents.GetAsync(SharePointData.QualityManualId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revise_ArchivesThenMovesEveryRevisionWhenRetiredContentIsIncludedAndDocumentBecomesRetired()
    {
        var harness = new PersistenceHarness(useLifecycleFolders: true);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision1Path, "Manuals/Retired/Quality Manual - 001 - 2023-06-01 through 2024-01-01.pdf"));
        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision2Path, "Manuals/Retired/Quality Manual - 002 - 2024-01-01 through 2024-07-01.pdf"));
        Assert.Single(harness.Files.UploadedPaths, "Manuals/Retired/Quality Manual - 005 - 2024-07-01 through 2024-09-01.docx");
    }

    [Fact]
    public async Task Revise_DeletesThePreviousRevisionWhenArchivedContentIsExcluded()
    {
        var harness = new PersistenceHarness(includeArchived: false);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Revise(5, true, "docx", PersistenceHarness.Contents("v5"), Published, null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(SharePointData.TravelPolicyPath, harness.Files.DeletedPaths);
        Assert.DoesNotContain(harness.Files.Moves, m => m.OldPath == SharePointData.TravelPolicyPath);
        Assert.Null(harness.Metadata.FileUpdateAt("Manuals/Archived/Policies/Travel Policy - 003 - 2024-03-01 through 2024-07-01.docx"));

        const string expected = "Manuals/Current/Policies/Travel Policy - 005 - 2024-07-01.docx";

        Assert.Contains(expected, harness.Files.UploadedPaths);
    }

    [Fact]
    public async Task Retire_DeletesEveryPreviousRevisionWhenArchivedContentIsExcludedEvenIfRetiredContentIsIncluded()
    {
        var harness = new PersistenceHarness(includeArchived: false, includeRetired: true);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Retire(Retired);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(SharePointData.QualityManualRevision1Path, harness.Files.DeletedPaths);
        Assert.Contains(SharePointData.QualityManualRevision2Path, harness.Files.DeletedPaths);
        Assert.DoesNotContain(harness.Files.Moves, m => m.OldPath == SharePointData.QualityManualRevision1Path);
        Assert.DoesNotContain(harness.Files.Moves, m => m.OldPath == SharePointData.QualityManualRevision2Path);
    }
}