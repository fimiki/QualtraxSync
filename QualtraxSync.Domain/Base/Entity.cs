using System.Collections.Concurrent;

namespace QualtraxSync.Domain.Base;

public abstract class Entity
{
    public IEnumerable<INotification> DomainEvents => _domainEvents.Keys.AsEnumerable();
    private readonly ConcurrentDictionary<INotification, byte> _domainEvents = new();

    protected virtual void AddDomainEvent(INotification eventItem)
    {
        _domainEvents.TryAdd(eventItem, 0);
    }

    public virtual void RemoveDomainEvent(INotification eventItem)
    {
        _domainEvents.TryRemove(eventItem, out _);
    }
}

public abstract class Entity<T> : Entity where T : struct
{
    int? _requestedHashCode;
    T _Id;
    public virtual T Id
    {
        get
        {
            return _Id;
        }
        protected set
        {
            _Id = value;
        }
    }


    public bool IsTransient()
    {
        return default(T).Equals(Id);
    }

    public override int GetHashCode()
    {
        if (!IsTransient())
        {
            if (!_requestedHashCode.HasValue)
                _requestedHashCode = Id.GetHashCode() ^ 31; // XOR for random distribution (http://blogs.msdn.com/b/ericlippert/archive/2011/02/28/guidelines-and-rules-for-gethashcode.aspx)

            return _requestedHashCode.Value;
        }
        else
            return base.GetHashCode();

    }

    public override bool Equals(object? obj)
    {
        if (obj == null || obj is not Entity<T>)
            return false;

        if (ReferenceEquals(this, obj))
            return true;

        if (GetType() != obj.GetType())
            return false;

        Entity<T> item = (Entity<T>)obj;

        if (item.IsTransient() || IsTransient())
            return false;
        else
            return Equals(item.Id, Id);
    }

    public static bool operator ==(Entity<T>? left, Entity<T>? right)
    {
        if (Equals(left, null))
            return Equals(right, null);
        else
            return left.Equals(right);
    }

    public static bool operator !=(Entity<T>? left, Entity<T>? right)
    {
        return !(left == right);
    }
}