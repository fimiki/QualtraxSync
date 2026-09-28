using QualtraxSync.Domain.Entities;
using QualtraxSync.Domain.Repositories;
using QualtraxSync.Persistence.Services;
using QualtraxSync.Persistence.Services.Materializers;

namespace QualtraxSync.Persistence.Repositories;

public class FolderRepository : IFolderRepository
{
    private readonly EntityStore<Folder, int> folders;
    private readonly FolderRoots roots;
    private readonly Initializer initializer;

    public FolderRepository(EntityStore<Folder, int> folders, FolderRoots roots, Initializer initializer) 
    {
        this.folders = folders;
        this.roots = roots;
        this.initializer = initializer;

        Add(Folder.Retired);
        Add(Folder.Unretired);
    }

    public async Task<IEnumerable<Folder>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        await initializer.Initializing;
        return roots.GetAll(folders);
    }

    public async Task<Folder?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await initializer.Initializing;
        return folders.Get(id);
    }

    public void Add(Folder folder)
    {
        folders.Add(folder);
        if (folder.Parent == null) roots.Add(folder);
    }
}
