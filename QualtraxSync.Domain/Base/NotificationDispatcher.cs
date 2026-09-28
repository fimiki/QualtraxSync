namespace QualtraxSync.Domain.Base;

public class NotificationDispatcher(IEnumerable<INotificationHandler> handlers)
{
    public async Task DispatchAsync(IEnumerable<Entity> entities, CancellationToken cancellationToken = default)
    {
        var notificationEntries = new List<(Entity Entity, INotification Notification)>();

        foreach (var entity in entities)
        {
            if (entity.DomainEvents.Any() == false) continue;

            foreach (var notification in entity.DomainEvents.ToList())
            {
                notificationEntries.Add((entity, notification));
            }
        }

        if (notificationEntries.Count == 0) return;

        var orderedHandlers = TopologicalSort(handlers);

        // determine how many applicable handlers each notification has, so we know when it's safe to remove it
        var pendingHandlerCounts = new Dictionary<INotification, int>();
        foreach (var (_, notification) in notificationEntries)
        {
            var notificationType = notification.GetType();
            pendingHandlerCounts[notification] = orderedHandlers.Count(h => GetHandledNotificationTypes(h).Contains(notificationType));
        }

        // remove notifications immediately if no handler applies to them
        foreach (var (entity, notification) in notificationEntries.Where(e => pendingHandlerCounts[e.Notification] == 0).ToList())
        {
            entity.RemoveDomainEvent(notification);
        }

        // outermost loop is the ordered handlers, ensuring each handler's dependencies have fully
        // completed processing (for all entities/notifications) before it begins execution
        foreach (var handler in orderedHandlers)
        {
            if (cancellationToken.IsCancellationRequested) return;

            await ExecuteHandlerAsync(handler, notificationEntries, pendingHandlerCounts, cancellationToken);
        }

        // dispatch any events that were raised in processing
        await DispatchAsync(entities, cancellationToken);
    }

    private static List<INotificationHandler> TopologicalSort(IEnumerable<INotificationHandler> handlers)
    {
        var result = new List<INotificationHandler>();
        var visited = new HashSet<INotificationHandler>();
        var visiting = new HashSet<INotificationHandler>();

        void Visit(INotificationHandler handler)
        {
            if (!visited.Add(handler))
            {
                return;
            }

            if (!visiting.Add(handler))
            {
                throw new InvalidOperationException($"Circular dependency detected involving handler '{handler.GetType().Name}'.");
            }

            foreach (var dependency in handlers.Where(h => handler.Dependencies.Contains(h.GetType())))
            {
                Visit(dependency);
            }

            visiting.Remove(handler);
            result.Add(handler);
        }

        foreach (var handler in handlers)
        {
            Visit(handler);
        }

        return result;
    }

    private static IEnumerable<Type> GetHandledNotificationTypes(INotificationHandler handler) =>
        handler.GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0]);

    private static async Task ExecuteHandlerAsync(
        INotificationHandler handler,
        List<(Entity Entity, INotification Notification)> notificationEntries,
        Dictionary<INotification, int> pendingHandlerCounts,
        CancellationToken cancellationToken)
    {
        var handledTypes = GetHandledNotificationTypes(handler).ToHashSet();
        var applicableEntries = notificationEntries.Where(e => handledTypes.Contains(e.Notification.GetType())).ToList();

        if (applicableEntries.Count == 0) return;

        var maxConcurrent = Math.Max(1, handler.ConcurrentExecutions);
        var running = new List<(Task Task, Entity Entity, INotification Notification)>();
        var faults = new List<Exception>();
        var stopLaunching = false;

        void CompleteEntry((Task Task, Entity Entity, INotification Notification) entry)
        {
            if (entry.Task.IsFaulted)
            {
                faults.AddRange(entry.Task.Exception?.InnerExceptions ?? [new Exception("Handler completion failed with an unknown error.")]);
                return;
            }

            lock (pendingHandlerCounts)
            {
                if (--pendingHandlerCounts[entry.Notification] == 0)
                {
                    entry.Entity.RemoveDomainEvent(entry.Notification);
                }
            }
        }

        foreach (var (entity, notification) in applicableEntries)
        {
            if (stopLaunching || cancellationToken.IsCancellationRequested) break;
            if (entity.DomainEvents.Contains(notification) == false) continue; // notification may have been removed by an upstream handler

            while (running.Count >= maxConcurrent)
            {
                var completed = await Task.WhenAny(running.Select(r => r.Task));
                var entry = running.First(r => r.Task == completed);
                running.Remove(entry);
                CompleteEntry(entry);

                if (entry.Task.IsFaulted)
                {
                    // stop launching new handler invocations, but don't abandon the ones already in flight -
                    // they'll be drained below so none are left orphaned/unobserved.
                    stopLaunching = true;
                    break;
                }
            }

            if (stopLaunching) break;

            var task = InvokeHandlerAsync(handler, notification, cancellationToken);

            running.Add((task, entity, notification));
        }

        // drain every in-flight task before returning/throwing so no task from this dispatch round is ever left running unobserved in the background
        foreach (var entry in running)
        {
            try
            {
                await entry.Task;
            }
            finally
            {
                CompleteEntry(entry);
            }
        }

        if (faults.Count > 0)
        {
            throw new AggregateException(faults);
        }
    }

    private static Task InvokeHandlerAsync(INotificationHandler handler, INotification notification, CancellationToken cancellationToken)
    {
        var notificationType = notification.GetType();
        var method = typeof(HandlerInvoker).GetMethod(nameof(HandlerInvoker.Invoke))?.MakeGenericMethod(notificationType);
        return (Task)method!.Invoke(null, [handler, notification, cancellationToken])!;
    }

    private static class HandlerInvoker
    {
        public static Task Invoke<T>(INotificationHandler handler, T notification, CancellationToken cancellationToken) where T : INotification
        {
            return ((INotificationHandler<T>)handler).HandleAsync(notification, cancellationToken);
        }
    }
}