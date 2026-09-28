using Microsoft.Extensions.Logging.Abstractions;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Domain.Repositories;
using QualtraxSync.Persistence.Handlers.Documents;
using QualtraxSync.Persistence.Handlers.Folders;
using QualtraxSync.Persistence.Handlers.Revisions;
using QualtraxSync.Persistence.Repositories;
using QualtraxSync.Persistence.Services;
using QualtraxSync.Persistence.Services.Materializers;
using PersistenceOptions = QualtraxSync.Persistence.Options;

namespace QualtraxSync.Persistence.Test.TestHelpers;

/// <summary>
/// Wires up the persistence stack (materializers, stores, repositories and notification handlers) over fake SharePoint
/// services so that a full retrieve -> materialize -> manipulate -> persist round trip can be exercised in a test.
/// </summary>
public sealed class PersistenceHarness
{
    public const string DriveId = "test-drive";

    private readonly EntityStore<Folder, int> _folders = new();
    private readonly EntityStore<Document, int> _documents = new();
    private readonly EntityStore<Revision, RevisionKey> _revisions = new();
    private readonly FolderRoots _roots = new();

    public PersistenceHarness(bool useLifecycleFolders = true, bool includeRetired = true, bool includeArchived = true, Dictionary<string, string[]>? folderMappings = null)
    {
        Options = Microsoft.Extensions.Options.Options.Create(new PersistenceOptions
        {
            Site = new SiteOptions { DriveId = DriveId },
            Lifecycle = new LifecycleOptions { UseFolders = useLifecycleFolders, IncludeRetired = includeRetired, IncludeArchived = includeArchived },
            FolderMappings = folderMappings ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { "Other", [] } }
        });
        PathService = new PathService(Options);

        SharePointData.Seed(Metadata);

        var folderMaterializer = new FolderMaterializer(_folders, _roots, PathService, Options, Metadata);
        var documentMaterializer = new DocumentMaterializer(_documents, folderMaterializer);
        var revisionMaterializer = new RevisionMaterializer(_revisions, documentMaterializer);

        var initializer = new Initializer(Options, Metadata, revisionMaterializer, documentMaterializer, folderMaterializer);

        Folders = new FolderRepository(_folders, _roots, initializer);
        Documents = new DocumentRepository(_documents, initializer);


        INotificationHandler[] handlers = [
            new FolderCreatedHandler(NullLogger<FolderCreatedHandler>.Instance, Options, Metadata, PathService),
            new FolderMovedHandler(NullLogger<FolderMovedHandler>.Instance, Options, Files, _roots, PathService),
            new FolderRemovedHandler(NullLogger<FolderRemovedHandler>.Instance, Options, Files, _folders, _roots, PathService),
            new FolderUpdatedHandler(NullLogger<FolderUpdatedHandler>.Instance, Options, Metadata, Files, PathService),
            new DocumentMovedHandler(NullLogger<DocumentMovedHandler>.Instance, Options, Files, Metadata, PathService),
            new DocumentRemovedHandler(NullLogger<DocumentRemovedHandler>.Instance, Options, Files, _documents, _revisions, PathService),
            new RevisionCreatedHandler(NullLogger<RevisionCreatedHandler>.Instance, Options, Files, Metadata, _revisions, PathService),
            new RevisionArchivedHandler(NullLogger<RevisionArchivedHandler>.Instance, Options, Files, Metadata, _revisions, PathService),
            new RevisionReactivatedHandler(NullLogger<RevisionReactivatedHandler>.Instance, Options, Files, Metadata, _revisions, PathService)
        ];

        UnitOfWork = new UnitOfWork(_folders, _documents, new NotificationDispatcher(handlers));
    }

    public FakeMetadataService Metadata { get; } = new();

    public FakeFileService Files { get; } = new();

    public Microsoft.Extensions.Options.IOptions<PersistenceOptions> Options { get; }

    public PathService PathService { get; }

    public IFolderRepository Folders { get; }

    public IDocumentRepository Documents { get; }

    public IUnitOfWork UnitOfWork { get; }

    /// <summary>
    /// Materializes the SharePoint content and returns the single root folder.
    /// </summary>
    public async Task<Folder> RootAsync()
    {
        var roots = await Folders.GetRootsAsync();
        return roots.Single(r => r.Id == SharePointData.ManualsId);
    }

    public async Task<Folder> FolderAsync(int id) => await Folders.GetAsync(id) ?? throw new InvalidOperationException($"Folder {id} was not materialized.");

    public async Task<Document> DocumentAsync(int id) => await Documents.GetAsync(id) ?? throw new InvalidOperationException($"Document {id} was not materialized.");

    /// <summary>
    /// Materializes all SharePoint content and discards anything recorded by the fakes so that only the changes made by
    /// a test are asserted on.
    /// </summary>
    public async Task LoadAsync()
    {
        await Folders.GetRootsAsync();

        Files.Reset();
        Metadata.Reset();
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => UnitOfWork.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// All entities currently tracked by the unit of work.
    /// </summary>
    public IEnumerable<Entity> Tracked() => ((UnitOfWork)UnitOfWork).GetAll();

    public static Stream Contents(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
}
