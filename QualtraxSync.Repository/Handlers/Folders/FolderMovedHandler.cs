using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Handlers.Documents;
using QualtraxSync.Persistence.Services.Materializers;
using QualtraxSync.Persistence.Services;
using System.Collections.Concurrent;

namespace QualtraxSync.Persistence.Handlers.Folders;

/// <summary>
/// Persists the moving or renaming of a folder
/// </summary>
public class FolderMovedHandler(
    ILogger<FolderMovedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    FolderRoots roots,
    PathService pathService) : INotificationHandler<FolderMovedNotification>, INotificationHandler<FolderRenamedNotification>
{
    private readonly ConcurrentDictionary<Folder, Lazy<Task>> _processing = new();

    /// <summary>
    /// Prioritize the removal of documents before moving or renaming folders
    /// </summary>
    public IEnumerable<Type> Dependencies => [typeof(DocumentRemovedHandler)];

    public Task HandleAsync(FolderMovedNotification notification, CancellationToken cancellationToken = default)
    {
        return _processing.GetOrAdd(notification.Folder, _ => new Lazy<Task>(() => ProcessAsync(notification.Folder, cancellationToken))).Value;
    }

    public Task HandleAsync(FolderRenamedNotification notification, CancellationToken cancellationToken = default)
    {
        return _processing.GetOrAdd(notification.Folder, _ => new Lazy<Task>(() => ProcessAsync(notification.Folder, cancellationToken))).Value;
    }

    private async Task ProcessAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        var renameEvent = folder.DomainEvents.OfType<FolderRenamedNotification>().FirstOrDefault();
        var moveEvent = folder.DomainEvents.OfType<FolderMovedNotification>().FirstOrDefault();
        var oldName = renameEvent?.OldName;

        var title = oldName ?? folder.Name;
        var driveId = options.Value.Site.DriveId;

        foreach (var lifecycle in folder.Lifecycles(options.Value.Lifecycle.UseFolders))
        {
            var newPath = pathService.Path(folder, true, lifecycle);
            var oldPath = pathService.Path(folder, false, lifecycle);

            await fileService.MoveAsync(driveId, oldPath, newPath, cancellationToken);

            if (moveEvent != null)
            {
                logger.LogInformation("Moved {Folder} {Id} from {OldPath} to {NewPath}", title, folder.Id, oldPath, newPath);

                if (moveEvent.OldParent?.Dependents() == false)
                {
                    moveEvent.OldParent.Remove(folder.Modified);
                }
            }

            if (renameEvent != null)
            {
                logger.LogInformation("Renamed {Folder} {Id} to {NewTitle}", title, folder.Id, folder.Name);
            }
        }

        roots.Refresh(folder);
    }
}