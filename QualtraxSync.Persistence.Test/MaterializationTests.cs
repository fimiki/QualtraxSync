using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Test.TestHelpers;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates that files and folders retrieved from IMetadataService are materialized into the domain entities.
/// </summary>
public class MaterializationTests
{
    [Fact]
    public async Task Folders_AreMaterializedFromMetadata()
    {
        var harness = new PersistenceHarness();

        var root = await harness.RootAsync();

        Assert.Equal(SharePointData.ManualsId, root.Id);
        Assert.Equal(SharePointData.ManualsName, root.Name);
        Assert.Null(root.Parent);
        Assert.Equal(SharePointData.ManualsCreated, root.Created);
        Assert.Equal(SharePointData.ManualsModified, root.Modified);

        var child = Assert.Single(root.Children);

        Assert.Equal(SharePointData.PoliciesId, child.Id);
        Assert.Equal(SharePointData.PoliciesName, child.Name);
        Assert.Same(root, child.Parent);
        Assert.Equal(SharePointData.PoliciesCreated, child.Created);
        Assert.Equal(SharePointData.PoliciesModified, child.Modified);
    }

    [Fact]
    public async Task LifecycleFolders_AreNotMaterializedAsSeparateFolders()
    {
        var harness = new PersistenceHarness();

        var roots = await harness.Folders.GetRootsAsync(TestContext.Current.CancellationToken);
        var folders = roots.SelectMany(r => r.Hierachy().Concat(r.Children)).Distinct().ToList();

        Assert.DoesNotContain(folders, f => f.Name == Lifecycle.Current.Name || f.Name == Lifecycle.Archived.Name || f.Name == Lifecycle.Retired.Name);
    }

    [Fact]
    public async Task Documents_AreMaterializedIntoTheirFolders()
    {
        var harness = new PersistenceHarness();

        var root = await harness.RootAsync();
        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        var qualityManual = await harness.DocumentAsync(SharePointData.QualityManualId);
        var travelPolicy = await harness.DocumentAsync(SharePointData.TravelPolicyId);
        var oldForm = await harness.DocumentAsync(SharePointData.OldFormId);

        Assert.Equal(SharePointData.QualityManualName, qualityManual.Name);
        Assert.Same(root, qualityManual.Folder);
        Assert.Equal(SharePointData.QualityManualCreated, qualityManual.Created);
        Assert.False(qualityManual.Retired);

        Assert.Same(policies, travelPolicy.Folder);
        Assert.False(travelPolicy.Retired);

        Assert.Same(root, oldForm.Folder);
        Assert.True(oldForm.Retired);

        Assert.Contains(qualityManual, root.Documents);
        Assert.Contains(oldForm, root.Documents);
        Assert.Contains(travelPolicy, policies.Documents);
    }

    [Fact]
    public async Task Revisions_AreMaterializedIntoTheirDocuments()
    {
        var harness = new PersistenceHarness();

        var qualityManual = await harness.DocumentAsync(SharePointData.QualityManualId);

        Assert.Equal(2, qualityManual.Revisions.Count);

        var first = qualityManual.Revisions.Single(r => r.Id.Revision == SharePointData.QualityManualRevision1);
        var second = qualityManual.Revisions.Single(r => r.Id.Revision == SharePointData.QualityManualRevision2);

        Assert.Equal("pdf", first.Extension);
        Assert.Equal(SharePointData.QualityManualRevision1Published, first.Published);
        Assert.Equal(SharePointData.QualityManualRevision1Archived, first.Archived);
        Assert.Equal(Lifecycle.Archived, first.Lifecycle());

        Assert.Equal(SharePointData.QualityManualRevision2Published, second.Published);
        Assert.Null(second.Archived);
        Assert.Equal(Lifecycle.Current, second.Lifecycle());

        var travelPolicy = await harness.DocumentAsync(SharePointData.TravelPolicyId);
        Assert.Equal("docx", Assert.Single(travelPolicy.Revisions).Extension);

        var oldForm = await harness.DocumentAsync(SharePointData.OldFormId);
        Assert.Equal(Lifecycle.Retired, Assert.Single(oldForm.Revisions).Lifecycle());
    }

    [Fact]
    public async Task MaterializedRevisions_ResolveToTheSharePointPathsTheyWereReadFrom()
    {
        var harness = new PersistenceHarness();

        var root = await harness.RootAsync();

        var paths = root.GetAllDocuments()
            .SelectMany(d => d.Revisions)
            .Select(r => r.CurrentPath(harness.PathService))
            .OrderBy(p => p)
            .ToList();

        Assert.Equal(harness.Metadata.SeededFilePaths.OrderBy(p => p), paths);
    }

    [Fact]
    public async Task MaterializedFolders_ResolveToTheSharePointPathsTheyWereReadFrom()
    {
        var harness = new PersistenceHarness();

        var root = await harness.RootAsync();
        var policies = await harness.FolderAsync(SharePointData.PoliciesId);

        Assert.Equal(SharePointData.ManualsCurrentPath, root.CurrentPath(Lifecycle.Current, harness.PathService));
        Assert.Equal(SharePointData.ManualsArchivedPath, root.CurrentPath(Lifecycle.Archived, harness.PathService));
        Assert.Equal(SharePointData.ManualsRetiredPath, root.CurrentPath(Lifecycle.Retired, harness.PathService));
        Assert.Equal(SharePointData.PoliciesCurrentPath, policies.CurrentPath(Lifecycle.Current, harness.PathService));
    }

    [Fact]
    public async Task Materialization_DoesNotRaiseDomainEvents()
    {
        var harness = new PersistenceHarness();

        await harness.Folders.GetRootsAsync(TestContext.Current.CancellationToken);

        Assert.All(harness.Tracked(), entity => Assert.Empty(entity.DomainEvents));
    }

    [Fact]
    public async Task Materialization_DoesNotWriteToSharePoint()
    {
        var harness = new PersistenceHarness();

        await harness.Folders.GetRootsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(harness.Metadata.Updates);
        Assert.Empty(harness.Files.Moves);
        Assert.Empty(harness.Files.Uploads);
        Assert.Empty(harness.Files.Deletes);
    }
}
