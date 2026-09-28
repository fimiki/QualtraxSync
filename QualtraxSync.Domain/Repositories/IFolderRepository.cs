using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;

namespace QualtraxSync.Domain.Repositories;

public interface IFolderRepository : IRepository
{
    public Task<IEnumerable<Folder>> GetRootsAsync(CancellationToken cancellationToken = default);

    public Task<Folder?> GetAsync(int id, CancellationToken cancellationToken = default);
    
    public void Add(Folder folder);
}
