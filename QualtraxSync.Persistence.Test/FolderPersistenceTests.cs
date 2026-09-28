using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Test.TestHelpers;
using System.Text;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates that manipulating materialized folders is persisted back to SharePoint for every lifecycle copy of the folder.
/// </summary>
public class FolderPersistenceTests
{
    private static readonly DateTimeOffset Modified = SharePointData.Local(2024, 8, 1);

    [Fact]
    public async Task Rename_MovesEveryPreviousLifecycleCopyOfTheFolder()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);
        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Retire(SharePointData.Local(2024, 7, 1));
        policies.Rename("Procedures", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved("Manuals/Current/Policies", "Manuals/Current/Procedures"));
        Assert.False(harness.Files.Moved("Manuals/Archived/Policies", "Manuals/Archived/Procedures"));
        Assert.False(harness.Files.Moved("Manuals/Retired/Policies", "Manuals/Retired/Procedures"));
        Assert.True(harness.Files.Moved("Manuals/Current/Procedures/Travel Policy - 003 - 2024-03-01.docx", "Manuals/Retired/Procedures/Travel Policy - 003 - 2024-03-01 through 2024-07-01.docx"));
    }

    [Fact]
    public async Task Rename_RewritesTheFolderMetadataAtTheNewPath()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Rename("Procedures", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        var metadata = harness.Metadata.FolderUpdateAt("Manuals/Current/Procedures");

        Assert.NotNull(metadata);
        Assert.Equal(SharePointData.PoliciesId, metadata.Id);
        Assert.Equal(SharePointData.PoliciesCreated, metadata.Created);
        Assert.Equal(Modified, metadata.Modified);
    }

    [Fact]
    public async Task Rename_DoesNotRelocateTheDocumentsInTheFolder()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Rename("Procedures", Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(harness.Files.Moves, m => m.OldPath == SharePointData.TravelPolicyPath);
    }

    [Fact]
    public async Task Move_RelocatesEveryLifecycleCopyOfTheFolder()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        var newPolicy = new Document(100, "New Policy", SharePointData.Local(2024, 7, 1), policies, true, 1, "docx", StreamOf("New Policy"), Modified, null);
        newPolicy.Revise(2, true, "docx", StreamOf("New Policy v2"), SharePointData.Local(2024, 8, 1), null);

        var travelPolicy = await harness.DocumentAsync(SharePointData.TravelPolicyId);
        travelPolicy.Retire(SharePointData.Local(2024, 7, 1));

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        policies.Move(null, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved("Manuals/Current/Policies", "Policies/Current"));
        Assert.True(harness.Files.Moved("Manuals/Archived/Policies", "Policies/Archived"));
        Assert.True(harness.Files.Moved("Manuals/Retired/Policies", "Policies/Retired"));
    }

    [Fact]
    public async Task Move_RelocatesOnlyCurrentLifecycleCopyOfTheFolderIfOnlyCurrentExists()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Move(null, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Files.Moved("Manuals/Current/Policies", "Policies/Current"));
        Assert.False(harness.Files.Moved("Manuals/Archived/Policies", "Policies/Archived"));
        Assert.False(harness.Files.Moved("Manuals/Retired/Policies", "Policies/Retired"));
    }

    [Fact]
    public async Task Move_PromotesTheFolderToARoot()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        policies.Move(null, Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        var roots = await harness.Folders.GetRootsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(policies, roots);
        Assert.Null(policies.Parent);
    }

    [Fact]
    public async Task Remove_DeletesTheFolderAndStopsTrackingIt()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var policies = await harness.FolderAsync(SharePointData.PoliciesId);
        var document = await harness.DocumentAsync(SharePointData.TravelPolicyId);

        document.Remove(Modified);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(SharePointData.TravelPolicyPath, harness.Files.DeletedPaths);
        Assert.Null(await harness.Folders.GetAsync(SharePointData.PoliciesId, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(policies, (await harness.RootAsync()).Children);
    }

    [Fact]
    public async Task NewFolder_HasItsMetadataAppliedToEachLifecycleItContains()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var created = SharePointData.Local(2024, 7, 1);

        var folder = new Folder(3, "Forms", root, created, Modified);
        harness.Folders.Add(folder);

        var document = new Document(50, "Request Form", created, folder, true, 9, "pdf", PersistenceHarness.Contents("form"), Modified, null);
        harness.Documents.Add(document);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        var metadata = harness.Metadata.FolderUpdateAt("Manuals/Current/Forms");

        Assert.NotNull(metadata);
        Assert.Equal(3, metadata.Id);
        Assert.Equal(created, metadata.Created);
        Assert.Contains("Manuals/Current/Forms/Request Form - 009 - 2024-08-01.pdf", harness.Files.UploadedPaths);
    }

    [Fact]
    public async Task Retire_RemovesTheCurrentLifecycleCopyOnlyFromBranchesThatLoseIt()
    {
        var harness = new PersistenceHarness();
        await harness.LoadAsync();

        var root = await harness.RootAsync();
        var created = SharePointData.Local(2024, 1, 1);

        // a common ancestor with two branches, each holding a single, currently-published document
        var parent = new Folder(300, "Hierarchy", root, created, created);
        harness.Folders.Add(parent);

        var branchA = new Folder(301, "BranchA", parent, created, created);
        harness.Folders.Add(branchA);

        var branchB = new Folder(302, "BranchB", parent, created, created);
        harness.Folders.Add(branchB);

        var documentA = new Document(310, "Doc A", created, branchA, true, 1, "pdf", StreamOf("a"), created, null);
        harness.Documents.Add(documentA);

        var documentB = new Document(311, "Doc B", created, branchB, true, 1, "pdf", StreamOf("b"), created, null);
        harness.Documents.Add(documentB);

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        harness.Files.Reset();
        harness.Metadata.Reset();

        // retiring the only document in BranchA means it loses its Current status entirely, while BranchB (and
        // therefore the shared parent) still has a Current document
        documentA.Retire(SharePointData.Local(2024, 2, 1));

        await harness.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(harness.PathService.Path(branchA, true, Lifecycle.Current), harness.Files.DeletedPaths);
        Assert.DoesNotContain(harness.PathService.Path(branchB, true, Lifecycle.Current), harness.Files.DeletedPaths);
        Assert.DoesNotContain(harness.PathService.Path(parent, true, Lifecycle.Current), harness.Files.DeletedPaths);

        Assert.Contains(harness.Metadata.Updates, u => u.Path == harness.PathService.Path(branchA, true, Lifecycle.Retired));
        Assert.Contains(harness.Metadata.Updates, u => u.Path == harness.PathService.Path(parent, true, Lifecycle.Retired));
        Assert.Contains(harness.Metadata.Updates, u => u.Path == harness.PathService.Path(parent, true, Lifecycle.Current));
    }

    private static Stream StreamOf(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));
}
