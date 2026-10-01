using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Handlers.Revisions;

/// <summary>
/// Handles the removal of the archived date from a file/revision that has been reactivated and (if Lifecycle folders are enabled) moves the revisions to the appropriate folders
/// </summary>
public class RevisionReactivatedHandler(
    ILogger<RevisionReactivatedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    IMetadataService metadataService,
    EntityStore<Revision, RevisionKey> revisions,
    PathService pathService) : RevisionUpdate(options, fileService, metadataService, revisions, pathService), INotificationHandler<RevisionReactivatedNotification>
{
    private readonly Options _options = options.Value;

    public int ConcurrentExecutions => 20;

    public async Task HandleAsync(RevisionReactivatedNotification notification, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(notification.Revision.Id, cancellationToken);

        if (_options.Lifecycle.UseFolders && notification.Revision.Document.Revisions.Count > 1)
        {
            logger.LogInformation("{Document} {Id} has been unretired, all {Count} revisions will be moved", notification.Revision.Document.Name, notification.Revision.Document.Id, notification.Revision.Document.Revisions.Count);

            foreach (var revision in notification.Revision.Document.Revisions.Except([notification.Revision]))
            {
                await UpdateAsync(revision.Id, cancellationToken);
            }
        }
        else
        {
            logger.LogInformation("{Document} {Id} has been unretired", notification.Revision.Document.Name, notification.Revision.Document.Id);
        }
    }
}
