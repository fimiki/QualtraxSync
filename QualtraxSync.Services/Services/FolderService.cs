using QualtraxSync.Contracts.Models;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Domain.Repositories;
using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace QualtraxSync.Services.Services;

public interface IFolderService
{
    QualtraxItem? Map(Folder? folder);
    QualtraxItem GetRoot(QualtraxItem folder);
    Task<Folder> GetUpdatedAsync(QualtraxItem item, DateTimeOffset update, CancellationToken cancellationToken = default);
    Task<IEnumerable<Folder>> GetRootsAsync(CancellationToken cancellationToken = default);
}

public class FolderService(IFolderRepository folders) : IFolderService
{
    public Task<IEnumerable<Folder>> GetRootsAsync(CancellationToken cancellationToken = default) => folders.GetRootsAsync(cancellationToken);

    public async Task<Folder> GetUpdatedAsync(QualtraxItem item, DateTimeOffset update, CancellationToken cancellationToken = default)
    {
        // workaround for SharePoint issue where 'Forms' is a reserved folder name at the root level
        if (item.Folder == null && item.Name == "Forms") item = item with { Name = "Form Templates" }; 

        var existing = await folders.GetAsync(item.Id, cancellationToken);

        if (existing is not null)
        {
            return await UpdateFolder(item, existing, update, cancellationToken);
        }

        return await CreateFolder(item, update, cancellationToken);
    }

    [return: NotNullIfNotNull(nameof(folder))]
    public QualtraxItem? Map(Folder? folder)
    {
        if (folder is null) return null;

        var result = new QualtraxItem
        (
            folder.Id,
            folder.Name,
            folder.Parent is not null ? Map(folder.Parent) : null
        );

        return result;
    }

    public QualtraxItem GetRoot(QualtraxItem folder)
    {
        var current = folder;
        while (current.Folder is not null)
        {
            current = current.Folder;
        }
        return current;
    }

    private async Task<Folder> UpdateFolder(QualtraxItem item, Folder existing, DateTimeOffset update, CancellationToken cancellationToken)
    {
        if (existing.Name != FolderName(item, existing.Parent))
        {
            existing.Rename(FolderName(item, existing.Parent), update);
        }

        if (existing.Parent?.Id != item.Folder?.Id)
        {
            var newParent = item.Folder is not null ? await GetUpdatedAsync(item.Folder, update, cancellationToken) : null;
            existing.Move(newParent, update);
        }

        return existing;
    }

    private async Task<Folder> CreateFolder(QualtraxItem item, DateTimeOffset update, CancellationToken cancellationToken)
    {
        var roots = await folders.GetRootsAsync(cancellationToken);
        var estimatedCreation = DateTimeOffset.MinValue;

        // use the latest item created prior to this root folder to estimate the creation date
        foreach (var document in roots.SelectMany(e => e.GetAllDocuments().Where(d => d.Id < item.Id)))
        {
            if (document.Created > estimatedCreation)
            {
                estimatedCreation = document.Created;
            }
        }

        var parent = item.Folder is not null ? await GetUpdatedAsync(item.Folder, update, cancellationToken) : null;
        var result = new Folder(item.Id, FolderName(item, parent), parent, estimatedCreation, estimatedCreation);
        foreach(var orphaned in parent?.Children.Where(c => c.Id < -100 && c.Name == result.Name) ?? [])
        {
            foreach (var child in orphaned.Children.ToList())
            {
                child.Move(result, update);
            }
        }

        folders.Add(result);

        return result;
    }

    private static string FolderName(QualtraxItem item, Folder? parent)
    {
        if (parent == null) return item.Name;

        var iteration = 0;
        
        foreach(var id in parent.Children.Where(c => c.Name == item.Name && c.Id > -100).Select(c => c.Id).Append(item.Id).OrderBy(id => id))
        {
            iteration++;

            if (id == item.Id) break;
        }

        return iteration > 1 ? $"{item.Name} ({iteration})" : item.Name;
    }
}