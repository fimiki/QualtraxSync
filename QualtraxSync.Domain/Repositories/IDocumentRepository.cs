using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;

namespace QualtraxSync.Domain.Repositories;

public interface IDocumentRepository : IRepository
{
    public Task<Document?> GetAsync(int id, CancellationToken cancellationToken = default);

    public void Add(Document document);
}
