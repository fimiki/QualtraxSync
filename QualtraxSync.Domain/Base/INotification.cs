namespace QualtraxSync.Domain.Base;

public interface INotification;

public interface INotificationHandler
{
    /// <summary>
    /// The maximum number of concurrent executions allowed for this handler. Defaults to 1, meaning notifications will be processed sequentially.
    /// </summary>
    int ConcurrentExecutions => 1;

    /// <summary>
    /// The collection of other notification handlers that this handler depends on. These dependencies will be executed before this handler is invoked.
    /// </summary>
    IEnumerable<Type> Dependencies => [];
}

public interface INotificationHandler<T> : INotificationHandler where T : INotification
{
    /// <summary>
    /// Processes the given notification asynchronously
    /// </summary>
    /// <param name="notification"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task HandleAsync(T notification, CancellationToken cancellationToken = default);
}