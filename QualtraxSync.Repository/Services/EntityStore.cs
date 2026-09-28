using System.Collections.Concurrent;

namespace QualtraxSync.Persistence.Services;

public sealed class EntityStore<TEntity, TKey> where TKey : struct where TEntity : Domain.Base.Entity<TKey>
{
    private readonly ConcurrentDictionary<TKey, TEntity> _entities = new();

    public bool Empty => _entities.IsEmpty;

    public IEnumerable<TEntity> GetAll()
    {
        return _entities.Values;
    }

    public TEntity Add(TEntity entity)
    {
        if (_entities.TryAdd(entity.Id, entity)) return entity;

        return Get(entity.Id) ?? entity;
    }

    public bool Remove(TEntity entity)
    {
        return _entities.TryRemove(entity.Id, out _);
    }

    public TEntity? Get(TKey id)
    {
        _entities.TryGetValue(id, out var entity);
        return entity;
    }
}
