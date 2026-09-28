using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Handlers.Documents;

/// <summary>
/// Removes all revisions of a document from persistence
/// </summary>
public class DocumentRemovedHandler(
    ILogger<DocumentRemovedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    EntityStore<Document, int> documents,
    EntityStore<Revision, RevisionKey> revisions,
    PathService pathService) : INotificationHandler<DocumentRemovedNotification>
{
    public int ConcurrentExecutions => 20;

    public async Task HandleAsync(DocumentRemovedNotification notification, CancellationToken cancellationToken = default)
    {
        var document = notification.Document;
        var title = document.Name;
        var driveId = options.Value.Site.DriveId;

        foreach (var revision in document.Revisions.Select(r => revisions.Get(r.Id)))
        {
            // revisions may have never been added or already been removed by a previous handler, so skip if not found
            if (revision == null) continue;

            var path = pathService.Path(revision, false);

            await fileService.DeleteAsync(driveId, path, cancellationToken);

            revisions.Remove(revision);

            logger.LogInformation("Removed {Document} {Id}-{Revision} from {Path}", title, document.Id, revision.Id.Revision, path);
        }

        documents.Remove(document);

        if (document.Folder.Dependents() == false)
        {
            document.Folder.Remove(document.Folder.Modified);
        }
    }
}