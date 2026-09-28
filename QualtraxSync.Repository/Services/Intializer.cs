using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Persistence.Services.Materializers;

namespace QualtraxSync.Persistence.Services;

public class Initializer(
    IOptions<Options> options, 
    IMetadataService metadataService, 
    RevisionMaterializer revisionService,
    DocumentMaterializer documentService,
    FolderMaterializer folderService)
{
    private readonly Lock _lock = new();
    private static Task? _loading;

    /// <summary>
    /// Returns the task loading the SharePoint content, starting it on first use.
    /// </summary>
    /// <param name="reload">If true, a new load is triggered if the previous load has completed.</param>
    public Task Initializing(bool reload = false)
    {
        lock (_lock)
        {
            _loading ??= LoadAsync(options.Value.Site.DriveId, metadataService, revisionService, documentService, folderService, default);

            if (reload && _loading.IsCompleted)
            {
                _loading = LoadAsync(options.Value.Site.DriveId, metadataService, revisionService, documentService, folderService, default);
            }

            return _loading;
        }
    }

    private static async Task LoadAsync(string driveId, IMetadataService metadataService, RevisionMaterializer revisionService, DocumentMaterializer documentService, FolderMaterializer folderService, CancellationToken cancellation = default)
    {
        var getFiles = metadataService.GetAllAsync<Models.File>(driveId, cancellation);
        var getFolders = metadataService.GetAllAsync<Models.Folder>(driveId, cancellation);

        await Task.WhenAll(getFiles, getFolders);

        foreach (var file in getFiles.Result)
        {
            revisionService.Materialize(file, getFolders.Result);
        }

        revisionService.AddToParent();
        documentService.AddToParent();
        folderService.AddToParent();
    }
}
