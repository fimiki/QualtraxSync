using QualtraxSync.Domain.Entities;
using QualtraxSync.Domain.Repositories;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Repositories;

public class DocumentRepository(EntityStore<Document, int> documents, Initializer initializer) : IDocumentRepository
{
    public async Task<Document?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await initializer.Initializing(documents.Empty);
        return documents.Get(id);
    }

    public void Add(Document Document)
    {
        documents.Add(Document);
    }
}
