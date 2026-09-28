using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Handlers.Folders;
using QualtraxSync.Persistence.Handlers.Revisions;
using QualtraxSync.Persistence.Services;
using System.Collections.Concurrent;

namespace QualtraxSync.Persistence.Handlers.Documents;

/// <summary>
/// Handles the movement of all revisions of a document to a new folder and/or the renaming of all revisions of a document to a new name
/// </summary>
public class DocumentMovedHandler(
    ILogger<DocumentMovedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    IMetadataService metadataService,
    PathService pathService) : INotificationHandler<DocumentMovedNotification>, INotificationHandler<DocumentRenamedNotification>
{
    private readonly ConcurrentDictionary<Document, Lazy<Task>> _processing = new();

    public int ConcurrentExecutions => 20;

    /// <summary>
    /// Handle any changes to the folder structure before moving or renaming files
    /// </summary>
    public IEnumerable<Type> Dependencies => [typeof(FolderMovedHandler)];

    public Task HandleAsync(DocumentMovedNotification notification, CancellationToken cancellationToken = default)
    {
        return _processing.GetOrAdd(notification.Document, _ => new Lazy<Task>(() => ProcessAsync(notification.Document, null, notification.OldParent, cancellationToken))).Value;
    }

    public Task HandleAsync(DocumentRenamedNotification notification, CancellationToken cancellationToken = default)
    {
        return _processing.GetOrAdd(notification.Document, _ => new Lazy<Task>(() => ProcessAsync(notification.Document, notification.OldName, null, cancellationToken))).Value;
    }

    private async Task ProcessAsync(Document document, string? oldName, Folder? oldFolder, CancellationToken cancellationToken = default)
    {
        oldName ??= document.DomainEvents.OfType<DocumentRenamedNotification>().FirstOrDefault()?.OldName;
        oldFolder ??= document.DomainEvents.OfType<DocumentMovedNotification>().FirstOrDefault()?.OldParent;

        var title = oldName ?? document.Name;
        var driveId = options.Value.Site.DriveId;

        foreach (var revision in document.Revisions)
        {
            var newPath = pathService.Path(revision, true);
            var oldPath = pathService.Path(revision, false);

            await fileService.MoveAsync(driveId, oldPath, newPath, cancellationToken);

            if (oldFolder != null)
            {
                logger.LogInformation("Moved {Document} {Id}-{Revision} from {OldPath} to {NewPath}", title, document.Id, revision.Id.Revision, oldPath, newPath);

                if (oldFolder?.Dependents() == false)
                {
                    oldFolder.Remove(oldFolder.Modified);
                }
            }

            if (oldName != null)
            {
                await metadataService.UpdateAsync(driveId, newPath, Models.File.FromDomainEntity(revision), cancellationToken);

                logger.LogInformation("Renamed {Document} {Id}-{Revision} to {NewTitle}", title, document.Id, revision.Id.Revision, document.Name);
            }
        }
    }
}
