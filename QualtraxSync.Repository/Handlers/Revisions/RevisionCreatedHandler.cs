using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Persistence.Services;
using QualtraxSync.Persistence.Handlers.Documents;
using QualtraxSync.Persistence.Handlers.Folders;

namespace QualtraxSync.Persistence.Handlers.Revisions;

/// <summary>
/// Handles the creation of a new revision by uploading the file to the specified drive and applying Qualtrax attributes.
/// </summary>
public class RevisionCreatedHandler(
    ILogger<RevisionCreatedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    IMetadataService metadataService,
    EntityStore<Revision, RevisionKey> entities,
    PathService pathService) : INotificationHandler<RevisionCreatedNotification>
{
    public int ConcurrentExecutions => 20;

    /// <summary>
    /// Handle any changes to the folder structure or document location before adding files
    /// </summary>
    public IEnumerable<Type> Dependencies => [
        typeof(RevisionArchivedHandler),
        typeof(RevisionReactivatedHandler),
        typeof(DocumentMovedHandler),
        typeof(FolderMovedHandler)];

    public async Task HandleAsync(RevisionCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var revision = notification.Revision;

        if (revision.Document.Retired && options.Value.Lifecycle.IncludeRetired == false)
        {
            // if this revision resulted in the retirement of the document and retired documents should be excluded, remove the document (i.e.) all revisions from SharePoint
            revision.Document.Remove(notification.Revision.Archived.GetValueOrDefault());

            logger.LogInformation("Skipping upload of {Document} {Id}-{Revision} published on {Published} because it is retired", revision.Document.Name, revision.Document.Id, revision.Id, revision.Published);

            return;
        }

        var driveId = options.Value.Site.DriveId;
        var path = pathService.Path(revision, false);

        await fileService.UploadAsync(driveId, path, notification.Contents, revision.Document.Created, cancellationToken);
        await metadataService.UpdateAsync(driveId, path, Models.File.FromDomainEntity(revision), cancellationToken);

        entities.Add(revision);

        logger.LogInformation("Uploaded {Document} {Id}-{Revision} published on {Published} to {Path}", revision.Document.Name, revision.Document.Id, revision.Id.Revision, revision.Published, path);
    }
}
