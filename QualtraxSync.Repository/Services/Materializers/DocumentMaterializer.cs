using QualtraxSync.Domain.Entities;
using System.Collections.Concurrent;

namespace QualtraxSync.Persistence.Services.Materializers;

public class DocumentMaterializer(
    EntityStore<Document, int> entities, 
    FolderMaterializer materializer
    ) : MaterializerBase
{
    public Document? Materialize(Models.File file, string parentPath, Dictionary<string, Models.Folder> folders)
    {
        if (entities.Get(file.Id) is { } existing)
        {
            return existing;
        }

        if (materializer.GetParent(parentPath, folders) is not { } parent)
        {
            throw new InvalidOperationException($"Document {file.Title} {file.Id} has a parent path of {parentPath} that could not be materialized into a folder");
        }

        var materialized = (Document)Activator.CreateInstance(typeof(Document), nonPublic: true)!;

        SetProperty(materialized, nameof(Document.Id), file.Id);
        SetProperty(materialized, nameof(Document.Name), file.Title);
        SetProperty(materialized, nameof(Document.Folder), parent);
        SetProperty(materialized, nameof(Document.Created), file.Created);
        SetProperty(materialized, nameof(Document.Retired), IsRetired(parentPath));

        return entities.Add(materialized);
    }

    public void AddToParent()
    {
        foreach (var group in entities.GetAll().GroupBy(e => e.Folder))
        {
            var parent = group.Key;
            var children = new ConcurrentDictionary<Document, byte>(group.Select(e => new KeyValuePair<Document, byte>(e, 0)));

            SetProperty(parent, '_' + nameof(Folder.Documents).ToLower(), children);
        }
    }
}
