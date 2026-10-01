using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Repositories;

public class UnitOfWork(
    EntityStore<Folder, int> folders,
    EntityStore<Document, int> documents,
    EntityStore<Revision, RevisionKey> revisions,
    NotificationDispatcher dispatcher) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(GetAll(), cancellationToken);
    }

    public void ResetContext()
    {
        folders.ClearAll();
        documents.ClearAll();
        revisions.ClearAll();
    }

    public IEnumerable<Entity> GetAll()
    {
        foreach (var folder in folders.GetAll())
        {
            yield return folder;
        }

        foreach (var document in documents.GetAll())
        {
            yield return document;

            foreach (var revision in document.Revisions)
            {
                yield return revision;
            }
        }
    }
}
