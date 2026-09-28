using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Handlers.Revisions;

/// <summary>
/// Applies the archival date to a file that has been archived and (if Lifecycle folders are enabled) moves the file to the appropriate folder
/// If the archival of this revision resulted in the retirement of the document, all revisions will be moved to the appropriate folder
/// (if Lifecycle folders are enabled) or deleted (if retired documents are excluded).
/// </summary>
public class RevisionArchivedHandler(
    ILogger<RevisionArchivedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    IMetadataService metadataService,
    EntityStore<Revision, RevisionKey> revisions,
    PathService pathService) : RevisionUpdate(options, fileService, metadataService, revisions, pathService), INotificationHandler<RevisionArchivedNotification>
{
    private readonly Options _options = options.Value;

    public int ConcurrentExecutions => 20;

    public async Task HandleAsync(RevisionArchivedNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.Revision.Document.Retired)
        {
            if (_options.Lifecycle.UseFolders || _options.Lifecycle.IncludeRetired == false)
            {
                logger.LogInformation("{Document} {Id} has been retired and all {Count} revisions will be {Change}", notification.Revision.Document.Name, notification.Revision.Document.Id, notification.Revision.Document.Revisions.Count, _options.Lifecycle.IncludeRetired ? "moved" : "deleted");

                foreach (var revision in notification.Revision.Document.Revisions)
                {
                    await UpdateAsync(revision.Id, cancellationToken);
                }

                if (_options.Lifecycle.IncludeRetired == false)
                {
                    notification.Revision.Document.Remove(notification.Revision.Archived.GetValueOrDefault());
                }
            }
            else if (_options.Lifecycle.IncludeRetired)
            {
                logger.LogInformation("{Document} {Id} has been retired", notification.Revision.Document.Name, notification.Revision.Document.Id);
            }
        }
        else
        { 
            await UpdateAsync(notification.Revision.Id, cancellationToken);

            logger.LogInformation("{Document} {Id}-{Revision} archived as-of {Archived}", notification.Revision.Document.Name, notification.Revision.Document.Id, notification.Revision.Id.Revision, notification.Revision.Archived);
        }
    }
}

public class RevisionUpdate(
    IOptions<Options> options,
    IFileService fileService,
    IMetadataService metadataService,
    EntityStore<Revision, RevisionKey> revisions,
    PathService pathService)
{
    protected async Task UpdateAsync(RevisionKey id, CancellationToken cancellationToken = default)
    {
        if (revisions.Get(id) is not Revision revision) return;

        var driveId = options.Value.Site.DriveId;
        var oldPath = pathService.Path(revision, false);

        if (options.Value.Lifecycle.IncludeArchived == false || 
           (options.Value.Lifecycle.IncludeRetired == false && revision.Document.Retired))
        {
            await fileService.DeleteAsync(driveId, oldPath, cancellationToken);
            revisions.Remove(revision);
            return;
        }

        var newPath = pathService.Path(revision, true);

        await fileService.MoveAsync(driveId, oldPath, newPath, cancellationToken);
        await metadataService.UpdateAsync(driveId, newPath, Models.File.FromDomainEntity(revision), cancellationToken);
    }
}