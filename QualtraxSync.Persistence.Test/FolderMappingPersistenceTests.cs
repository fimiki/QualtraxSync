using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Test.TestHelpers;
using System.Text;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates the optional FolderMappings configuration, which places one or more Qualtrax root folders under a new
/// SharePoint-only root folder while leaving folders not covered by any mapping under 'Other'.
/// </summary>
public class FolderMappingPersistenceTests
{
    private static readonly DateTimeOffset Modified = SharePointData.Local(2024, 8, 1);

    private static Dictionary<string, string[]> Mappings(params (string SharePointFolder, string[] QualtraxRoots)[] mappings)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { "Other", [] } };

        foreach (var (sharePointFolder, qualtraxRoots) in mappings)
        {
            result[sharePointFolder] = qualtraxRoots;
        }

        return result;
    }

    [Fact]
    public async Task NoMappingsOtherThanOther_MirrorsRootFoldersDirectlyToTheDriveRoot()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Rename("Procedures", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(SharePointData.PoliciesCurrentPath, "Manuals/Current/Procedures"));
    }

    [Fact]
    public async Task MappedRootFolder_PersistsNewFolderUnderTheMappedSharePointFolder()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var manuals = await harness.RootAsync();

        var newFolder = new Folder(200, "New Folder", manuals, Modified, Modified);
        var newFile = new Document(300, "New File", Modified, newFolder, true, 1, "pdf", StreamOf("None"), Modified, null);
        harness.Folders.Add(newFolder);
        harness.Documents.Add(newFile); 

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Documentation/Manuals/Current/New Folder", harness.Metadata.FolderUpdatePaths);
    }

    [Fact]
    public async Task UnmappedRootFolder_PersistsNewFolderUnderTheOtherSharePointFolder()
    {
        var mappings = Mappings(("Documentation", ["Some Other Root"]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var manuals = await harness.RootAsync();

        var newFolder = new Folder(200, "New Folder", manuals, Modified, Modified);
        var newFile = new Document(300, "New File", Modified, newFolder, true, 1, "pdf", StreamOf("None"), Modified, null);
        harness.Folders.Add(newFolder);
        harness.Documents.Add(newFile);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Other/Manuals/Current/New Folder", harness.Metadata.FolderUpdatePaths);
    }

    [Fact]
    public async Task MappedRootFolder_RelocatesEveryLifecycleCopyOfAMovedSubFolder()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Rename("Procedures", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved("Documentation/Manuals/Current/Policies", "Documentation/Manuals/Current/Procedures"));
    }

    [Fact]
    public async Task MappedRootFolder_UploadsNewRevisionsUnderTheMappedSharePointFolder()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Revise(4, true, "docx", PersistenceHarness.Contents("v4"), SharePointData.Local(2024, 9, 1), null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Documentation/Manuals/Current/Policies/Travel Policy - 004 - 2024-09-01.docx", harness.Files.UploadedPaths);
    }

    [Fact]
    public async Task MappedRootFolder_ArchivesPreviousRevisionUnderTheMappedSharePointFolder()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.QualityManualId);

        document.Revise(3, true, "pdf", PersistenceHarness.Contents("v3"), SharePointData.Local(2024, 9, 1), null);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved(
            "Documentation/Manuals/Current/Quality Manual - 002 - 2024-01-01.pdf",
            "Documentation/Manuals/Archived/Quality Manual - 002 - 2024-01-01 through 2024-09-01.pdf"));
    }

    [Fact]
    public async Task MappedRootFolder_DeletesRevisionsUnderTheMappedSharePointFolderWhenDocumentIsRemoved()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Remove(Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Documentation/Manuals/Current/Policies/Travel Policy - 003 - 2024-03-01.docx", harness.Files.DeletedPaths);
    }

    [Fact]
    public async Task DifferentRootFolders_CanBeMappedToDifferentSharePointFolders()
    {
        var mappings = Mappings(("Documentation", [SharePointData.ManualsName]), ("Miscellaneous", ["Other Root"]));
        var harness = new PersistenceHarness(folderMappings: mappings);
        await harness.LoadAsync();

        var manuals = await harness.RootAsync();
        var otherRoot = new Folder(300, "Other Root", null, SharePointData.Local(2024, 1, 1), SharePointData.Local(2024, 1, 1));
        harness.Folders.Add(otherRoot);

        var manualsChild = new Folder(201, "Manuals Child", manuals, Modified, Modified);
        var manualsFile = new Document(301, "New File", Modified, manualsChild, true, 1, "pdf", StreamOf("None"), Modified, null);
        var otherChild = new Folder(202, "Other Child", otherRoot, Modified, Modified);
        var otherFile = new Document(302, "New File", Modified, otherChild, true, 1, "pdf", StreamOf("None"), Modified, null);

        harness.Folders.Add(manualsChild);
        harness.Folders.Add(otherChild);
        harness.Documents.Add(manualsFile);
        harness.Documents.Add(otherFile);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Documentation/Manuals/Current/Manuals Child", harness.Metadata.FolderUpdatePaths);
        Assert.Contains("Miscellaneous/Other Root/Current/Other Child", harness.Metadata.FolderUpdatePaths);
    }


    private static Stream StreamOf(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));
}
