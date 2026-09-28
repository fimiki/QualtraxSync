using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Test.TestHelpers;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates that manipulating materialized documents is persisted back to SharePoint using the paths the content was
/// originally retrieved from.
/// </summary>
public class DocumentPersistenceTests
{
    private static readonly DateTimeOffset Modified = SharePointData.Local(2024, 8, 1);

    [Fact]
    public async Task Rename_MovesEveryRevisionAndRewritesTheFileMetadata()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Rename("Quality Handbook", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        const string renamedRevision1 = "Manuals/Archived/Quality Handbook - 001 - 2023-06-01 through 2024-01-01.pdf";
        const string renamedRevision2 = "Manuals/Current/Quality Handbook - 002 - 2024-01-01.pdf";

        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision1Path, renamedRevision1));
        Assert.True(harness.Files.Moved(SharePointData.QualityManualRevision2Path, renamedRevision2));

        var metadata = harness.Metadata.FileUpdateAt(renamedRevision2);

        Assert.NotNull(metadata);
        Assert.Equal("Quality Handbook", metadata.Title);
        Assert.Equal(SharePointData.QualityManualId, metadata.Id);
        Assert.Equal(SharePointData.QualityManualRevision2, metadata.Revision);
        Assert.Equal(SharePointData.QualityManualRevision2Published, metadata.Published);
        Assert.Null(metadata.Archived);
        Assert.Equal(SharePointData.QualityManualCreated, metadata.Created);

        Assert.All(harness.Tracked(), entity => Assert.Empty(entity.DomainEvents));
    }

    [Fact]
    public async Task Rename_RefreshesTheFolderMetadataWithTheNewTimestamp()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Rename("Quality Handbook", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        var folder = harness.Metadata.FolderUpdateAt(SharePointData.ManualsCurrentPath);

        Assert.NotNull(folder);
        Assert.Equal(SharePointData.ManualsId, folder.Id);
        Assert.Equal(SharePointData.ManualsCreated, folder.Created);
        Assert.Equal(Modified, folder.Modified);
        Assert.NotEqual(SharePointData.ManualsModified, folder.Modified);
    }

    [Fact]
    public async Task Move_RelocatesEveryRevisionToTheNewFolder()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Move(root, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(SharePointData.TravelPolicyPath, "Manuals/Current/Travel Policy - 003 - 2024-03-01.docx"));
        Assert.Same(root, document.Folder);
        Assert.Contains(document, root.Documents);
    }

    [Fact]
    public async Task Move_DoesNotRewriteTheFileMetadata()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Move(root, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(harness.Metadata.Updates, u => u.Metadata is Models.File);
    }

    [Fact]
    public async Task Move_RemovesTheFolderThatIsLeftEmpty()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Move(root, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Null(await harness.Folders.GetAsync(SharePointData.PoliciesId, TestContext.Current.CancellationToken));
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task Remove_DeletesEveryRevisionFromThePathsItWasRetrievedFrom()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Remove(Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(SharePointData.QualityManualRevision1Path, harness.Files.DeletedPaths);
        Assert.Contains(SharePointData.QualityManualRevision2Path, harness.Files.DeletedPaths);
        Assert.Null(await harness.Documents.GetAsync(SharePointData.QualityManualId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewDocument_IsUploadedAndHasItsMetadataApplied()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var created = SharePointData.Local(2024, 7, 1);

        var document = new Document(40, "Safety Plan", created, root, true, 7, "pdf", PersistenceHarness.Contents("safety"), Modified, null);
        harness.Documents.Add(document);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        const string expected = "Manuals/Current/Safety Plan - 007 - 2024-08-01.pdf";

        Assert.Contains(expected, harness.Files.UploadedPaths);
        Assert.Equal("safety", harness.Files.Uploads.Single(u => u.Path == expected).Contents);
        Assert.Contains(harness.Metadata.Updates, u => u.Path == expected);
        Assert.Equal(PersistenceHarness.DriveId, harness.Files.Uploads.Single(u => u.Path == expected).DriveId);
    }
}
