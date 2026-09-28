using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;
using PersistenceOptions = QualtraxSync.Persistence.Options;

namespace QualtraxSync.Persistence.Test;

/// <summary>
/// Validates that <see cref="PathService"/> resolves the correct SharePoint paths for folders and revisions,
/// including lifecycle folders and root folder mappings, taking the place of the path tests previously on the domain entities.
/// </summary>
public class PathServiceTests
{
    private static readonly DateTimeOffset Utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PathService CreateService(bool useLifecycleFolders = true, Dictionary<string, string[]>? folderMappings = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PersistenceOptions
        {
            Lifecycle = new LifecycleOptions { UseFolders = useLifecycleFolders },
            FolderMappings = folderMappings ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { "Other", [] } }
        });

        return new PathService(options);
    }

    [Fact]
    public void Path_Folder_WithoutParent_ReturnsName()
    {
        var pathService = CreateService();
        var root = new Folder(1, "Root", null, Utc, Utc);

        Assert.Equal("Root", pathService.Path(root));
    }

    [Fact]
    public void Path_Folder_WithParent_ConcatenatesHierarchy()
    {
        var pathService = CreateService();
        var root = new Folder(1, "Root", null, Utc, Utc);
        var child = new Folder(2, "Child", root, Utc, Utc);

        Assert.Equal("Root/Child", pathService.Path(child));
    }

    [Fact]
    public void Path_Folder_WithLifecycle_AppendsLifecycleAtRoot()
    {
        var pathService = CreateService();
        var root = new Folder(1, "Root", null, Utc, Utc);
        var child = new Folder(2, "Child", root, Utc, Utc);

        Assert.Equal("Root/Archived/Child", pathService.Path(child, true, Lifecycle.Archived));
    }

    [Fact]
    public void Path_Folder_AfterRename_PreviousPathReturnsOriginalName()
    {
        var pathService = CreateService();
        var t1 = Utc.AddMinutes(5);
        var root = new Folder(1, "Root", null, Utc, Utc);
        var child = new Folder(2, "Old", root, Utc, Utc);
        var originalPath = pathService.Path(child);

        child.Rename("New", t1);

        Assert.Equal("Root/New", pathService.Path(child));
        Assert.Equal(originalPath, pathService.Path(child, false));
        Assert.Equal("Root/Old", pathService.Path(child, false));
    }

    [Fact]
    public void Path_Folder_AfterMove_PreviousPathReturnsOriginalParentPath()
    {
        var pathService = CreateService();
        var t1 = Utc.AddMinutes(10);

        var parentA = new Folder(1, "A", null, Utc, Utc);
        var parentB = new Folder(2, "B", null, Utc, Utc);
        var child = new Folder(3, "Child", parentA, Utc, Utc);
        var originalPath = pathService.Path(child);

        child.Move(parentB, t1);

        Assert.Equal("B/Child", pathService.Path(child));
        Assert.Equal(originalPath, pathService.Path(child, false));
        Assert.Equal("A/Child", pathService.Path(child, false));
    }

    [Fact]
    public void Path_Folder_WithRootFolderMapping_PrependsMappedFolder()
    {
        var pathService = CreateService(folderMappings: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Other", [] },
            { "Mapped", ["Root"] }
        });

        var root = new Folder(1, "Root", null, Utc, Utc);
        var child = new Folder(2, "Child", root, Utc, Utc);

        Assert.Equal("Mapped/Root/Child", pathService.Path(child));
    }

    [Fact]
    public void Path_Folder_WithUnmappedRoot_PrependsOtherFolder()
    {
        var pathService = CreateService(folderMappings: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Other", [] },
            { "Mapped", ["SomethingElse"] }
        });

        var root = new Folder(1, "Root", null, Utc, Utc);

        Assert.Equal("Other/Root", pathService.Path(root));
    }

    [Fact]
    public void Path_Revision_AfterDocumentMove_PreviousPathReturnsOriginalFolderPath()
    {
        var pathService = CreateService(useLifecycleFolders: false);
        var t1 = Utc.AddMinutes(10);

        var folderA = new Folder(1, "A", null, Utc, Utc);
        var folderB = new Folder(2, "B", null, Utc, Utc);
        var document = new Document(10, "Doc", Utc.DateTime, folderA, true, 1, "pdf", StreamOf("rev1"), Utc.AddMinutes(1), null);

        var revision = Assert.Single(document.Revisions);
        var originalPath = pathService.Path(revision);

        document.Move(folderB, t1);

        var name = revision.Name();
        var newPath = pathService.Path(revision);
        var oldPath = pathService.Path(revision, false);

        Assert.Equal("B", newPath[..(newPath.IndexOf(name) - 1)]);
        Assert.Equal(originalPath, oldPath);
        Assert.Equal("A", oldPath[..(oldPath.IndexOf(name) - 1)]);
    }

    [Fact]
    public void Path_Revision_WithLifecycle_AfterSupersededRevision_ReflectsArchivedState()
    {
        var pathService = CreateService();
        var root = new Folder(1, "Root", null, Utc, Utc);
        var document = new Document(10, "Doc", Utc.DateTime, root, true, 1, "pdf", StreamOf("rev1"), Utc.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions);

        ClearEvents(document, revision);

        document.Revise(2, true, "pdf", StreamOf("rev2"), Utc.AddMinutes(2), null);

        var newName = revision.Name();
        var newPath = pathService.Path(revision);

        var oldName = revision.Name(false);
        var oldPath = pathService.Path(revision, false);

        Assert.Equal("Root/Archived", newPath[..(newPath.IndexOf(newName) - 1)]);
        Assert.Equal("Root/Current", oldPath[..(oldPath.IndexOf(oldName) - 1)]);
    }

    [Fact]
    public void Path_Revision_WithLifecycleWithDeepFolderStructure_ReturnsExpectedPathAfterMoveAndRename()
    {
        var pathService = CreateService();
        var rootA = new Folder(1, "RootA", null, Utc, Utc);
        var subfolderA = new Folder(2, "SubfolderA", rootA, Utc, Utc);
        var finalFolderA = new Folder(3, "FinalFolderA", subfolderA, Utc, Utc);
        var document = new Document(10, "Doc", Utc.DateTime, finalFolderA, true, 1, "pdf", StreamOf("rev1"), Utc.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions);

        var revisionPath = pathService.Path(revision, true);
        Assert.Equal("RootA/Current/SubfolderA/FinalFolderA/" + revision.Name(), revisionPath);

        ClearEvents(document, revision);

        var rootB = new Folder(4, "RootB", null, Utc, Utc);

        subfolderA.Move(rootB, Utc.AddMinutes(2));
        finalFolderA.Rename("FinalFolderZ", Utc.AddMinutes(3));
        document.Revise(2, true, "pdf", StreamOf("rev2"), Utc.AddDays(1), null);

        var revision2 = document.Revisions.Single(r => r.Id.Revision == 2);

        Assert.Equal(revisionPath, pathService.Path(revision, false));
        Assert.Equal("RootB/Archived/SubfolderA/FinalFolderZ/" + revision.Name(), pathService.Path(revision, true));
        Assert.Equal("RootB/Current/SubfolderA/FinalFolderZ/" + revision2.Name(), pathService.Path(revision2, true));
    }

    private static void ClearEvents(params Domain.Base.Entity[] entities)
    {
        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents.ToList())
            {
                entity.RemoveDomainEvent(domainEvent);
            }
        }
    }

    private static Stream StreamOf(string text) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
}
