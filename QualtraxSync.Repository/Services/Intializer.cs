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
    private readonly Lazy<Task> _loading = new(() => LoadAsync(options.Value.Site.DriveId, metadataService, revisionService, documentService, folderService, default));

    public Task Initializing => _loading.Value;

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
