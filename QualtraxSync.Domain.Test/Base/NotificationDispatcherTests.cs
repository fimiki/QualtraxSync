using QualtraxSync.Domain.Base;
using System.Reflection;
using Xunit;

namespace QualtraxSync.Domain.Test.Base;

public class NotificationDispatcherTests
{
    private sealed class TestEntity : Entity
    {
        public void Raise(INotification notification) => AddDomainEvent(notification);
    }

    private sealed record FirstTestNotification(string Name) : INotification;

    private sealed record SecondTestNotification(string Name) : INotification;

    /// <summary>
    /// A handler whose <see cref="HandleAsync"/> raises additional notifications on a target entity the
    /// first time it runs, to verify the dispatcher re-dispatches events produced during processing.
    /// </summary>
    private sealed class RecordingHandler<T> : INotificationHandler<T> where T : INotification
    {
        private readonly List<T> _handled = new();
        private readonly object _lock = new();
        private readonly TaskCompletionSource<object?>? _gate;

        public RecordingHandler(int concurrentExecutions = 1, TaskCompletionSource<object?>? gate = null)
        {
            ConcurrentExecutions = concurrentExecutions;
            _gate = gate;
        }

        public int ConcurrentExecutions { get; }

        public IReadOnlyList<T> Handled
        {
            get { lock (_lock) { return _handled.ToList(); } }
        }

        public Func<T, Task>? OnHandle { get; set; }

        public async Task HandleAsync(T notification, CancellationToken cancellationToken = default)
        {
            if (_gate != null)
            {
                await _gate.Task;
            }

            if (OnHandle != null)
            {
                await OnHandle(notification);
            }

            lock (_lock)
            {
                _handled.Add(notification);
            }
        }
    }

    private sealed class ThrowingHandler<T> : INotificationHandler<T> where T : INotification
    {
        public Task HandleAsync(T notification, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler failed.");
        }
    }

    [Fact]
    public async Task DispatchAsync_RemovesNotification_AfterHandlerCompletes()
    {
        var entity = new TestEntity();
        var notification = new FirstTestNotification("one");
        entity.Raise(notification);

        var handler = new RecordingHandler<FirstTestNotification>();
        var dispatcher = new NotificationDispatcher([handler]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Empty(entity.DomainEvents);
        Assert.Single(handler.Handled);
        Assert.Equal(notification, handler.Handled[0]);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesNotifications_RaisedDuringProcessing()
    {
        var entity = new TestEntity();
        var first = new FirstTestNotification("first");
        entity.Raise(first);

        var secondHandler = new RecordingHandler<SecondTestNotification>();
        var firstHandler = new RecordingHandler<FirstTestNotification>
        {
            OnHandle = _ =>
            {
                // simulate a handler that raises a new notification while processing the original one
                entity.Raise(new SecondTestNotification("second"));
                return Task.CompletedTask;
            }
        };

        var dispatcher = new NotificationDispatcher([firstHandler, secondHandler]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Empty(entity.DomainEvents);
        Assert.Single(firstHandler.Handled);
        Assert.Single(secondHandler.Handled);
        Assert.Equal("second", secondHandler.Handled[0].Name);
    }

    [Fact]
    public async Task DispatchAsync_LimitsConcurrency_ToLowestConcurrentExecutionsAmongHandlers()
    {
        var entity = new TestEntity();
        for (var i = 0; i < 5; i++)
        {
            entity.Raise(new FirstTestNotification($"n{i}"));
        }

        var maxObservedConcurrency = 0;
        var currentConcurrency = 0;
        var lockObj = new object();

        var limitedHandler = new RecordingHandler<FirstTestNotification>(concurrentExecutions: 2)
        {
            OnHandle = async _ =>
            {
                lock (lockObj)
                {
                    currentConcurrency++;
                    maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentConcurrency);
                }

                await Task.Delay(50);

                lock (lockObj)
                {
                    currentConcurrency--;
                }
            }
        };

        var dispatcher = new NotificationDispatcher([limitedHandler]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Equal(5, limitedHandler.Handled.Count);
        Assert.True(maxObservedConcurrency <= 2, $"Expected max concurrency <= 2 but observed {maxObservedConcurrency}.");
    }

    [Fact]
    public async Task DispatchAsync_ProcessesMultipleHandlers_WithDifferentConcurrencyLevels()
    {
        var entity = new TestEntity();
        for (var i = 0; i < 4; i++)
        {
            entity.Raise(new FirstTestNotification($"n{i}"));
        }

        var handlerA = new RecordingHandler<FirstTestNotification>(concurrentExecutions: 1)
        {
            OnHandle = async _ => await Task.Delay(10)
        };
        var handlerB = new RecordingHandler<FirstTestNotification>(concurrentExecutions: 3)
        {
            OnHandle = async _ => await Task.Delay(10)
        };

        var dispatcher = new NotificationDispatcher([handlerA, handlerB]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Equal(4, handlerA.Handled.Count);
        Assert.Equal(4, handlerB.Handled.Count);
        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public async Task DispatchAsync_PropagatesException_WhenHandlerThrows()
    {
        var entity = new TestEntity();
        entity.Raise(new FirstTestNotification("boom"));

        var dispatcher = new NotificationDispatcher([new ThrowingHandler<FirstTestNotification>()]);

        await Assert.ThrowsAsync<TargetInvocationException>(() => dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_PropagatesException_WhenAnyOfMultipleConcurrentHandlersThrows()
    {
        var entity = new TestEntity();
        for (var i = 0; i < 3; i++)
        {
            entity.Raise(new FirstTestNotification($"n{i}"));
        }

        var throwingHandler = new ThrowingHandler<FirstTestNotification>();
        var succeedingHandler = new RecordingHandler<FirstTestNotification>(concurrentExecutions: 1);

        var dispatcher = new NotificationDispatcher([succeedingHandler, throwingHandler]);

        await Assert.ThrowsAsync<TargetInvocationException>(() => dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_DoesNotRemoveNotification_WhenOneOfMultipleHandlersFails()
    {
        var entity = new TestEntity();
        var notification = new FirstTestNotification("one");
        entity.Raise(notification);

        var succeedingHandler = new RecordingHandler<FirstTestNotification>();
        var throwingHandler = new ThrowingHandler<FirstTestNotification>();

        var dispatcher = new NotificationDispatcher([succeedingHandler, throwingHandler]);

        await Assert.ThrowsAsync<TargetInvocationException>(() => dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken));

        // onCompletion (which removes the domain event) should only run once ALL applicable handlers
        // for the notification have completed successfully; since one handler threw, the event must remain.
        Assert.Contains(notification, entity.DomainEvents);
    }

    [Fact]
    public async Task DispatchAsync_InvokesOnCompletion_OnlyAfterAllApplicableHandlersSucceed()
    {
        var entity = new TestEntity();
        var notification = new FirstTestNotification("one");
        entity.Raise(notification);

        var slowGate = new TaskCompletionSource<object?>();
        var slowHandler = new RecordingHandler<FirstTestNotification>(2, slowGate);
        var fastHandler = new RecordingHandler<FirstTestNotification>(2);

        var dispatcher = new NotificationDispatcher([fastHandler, slowHandler]);
        var dispatchTask = dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        // give the fast handler a chance to complete while the slow handler is still gated
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Single(fastHandler.Handled);
        Assert.Contains(notification, entity.DomainEvents);

        slowGate.SetResult(null);
        await dispatchTask;

        Assert.Single(slowHandler.Handled);
        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public async Task DispatchAsync_WithNoApplicableHandlers_RemovesNotificationImmediately()
    {
        var entity = new TestEntity();
        entity.Raise(new SecondTestNotification("orphan"));

        var dispatcher = new NotificationDispatcher([new RecordingHandler<FirstTestNotification>()]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public async Task DispatchAsync_WithNoEntitiesHavingNotifications_CompletesWithoutInvokingHandlers()
    {
        var entity = new TestEntity();
        var handler = new RecordingHandler<FirstTestNotification>();
        var dispatcher = new NotificationDispatcher([handler]);

        await dispatcher.DispatchAsync([entity], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Handled);
    }
}
