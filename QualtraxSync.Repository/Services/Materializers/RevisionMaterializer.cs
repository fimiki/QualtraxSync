using QualtraxSync.Domain.Entities;

namespace QualtraxSync.Persistence.Services.Materializers;

public class RevisionMaterializer(
    EntityStore<Revision, RevisionKey> entities, 
    DocumentMaterializer materializer) : MaterializerBase
{
    public Revision? Materialize(KeyValuePair<string, Models.File> item, Dictionary<string, Models.Folder> folders)
    {
        var file = item.Value;  

        if (entities.Get(GetId(file)) is { } existing)
        {
            return existing;
        }

        var (parentPath, name) = SplitPath(item.Key);
        var document = materializer.Materialize(file, parentPath, folders);

        if (document == null) return null;

        var materialized = (Revision)Activator.CreateInstance(typeof(Revision), nonPublic: true)!;

        SetProperty(materialized, nameof(Revision.Id), GetId(file));
        SetProperty(materialized, nameof(Revision.Extension), Path.GetExtension(name).TrimStart('.'));
        SetProperty(materialized, nameof(Revision.Published), file.Published);
        SetProperty(materialized, nameof(Revision.Archived), file.Archived);
        SetProperty(materialized, nameof(Revision.Document), document);

        return entities.Add(materialized);
    }

    public void AddToParent()
    {
        foreach (var group in entities.GetAll().GroupBy(e => e.Document))
        {
            var parent = group.Key;
            var children = group.OrderBy(r => r.Id).ToList();

            SetProperty(parent, '_' + nameof(Document.Revisions).ToLower(), children);
        }
    }

    private static RevisionKey GetId(Models.File file) => new(file.Id, file.Revision);
}