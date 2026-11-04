using Codex.Application.Events;
using Codex.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.WebApi.Tests;

[TestFixture]
public class InMemoryBookEventsTests
{
    [Test]
    [CancelAfter(10000)]
    public async Task Publish_WhenSubscribed_ShouldDeliver()
    {
        var bus = new InMemoryBookEvents(NullLogger<InMemoryBookEvents>.Instance);
        var bookId = Guid.NewGuid();
        var delivered = new TaskCompletionSource<BookEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe(bookId, e =>
        {
            delivered.TrySetResult(e);
            return Task.CompletedTask;
        });
        var sent = new ProseSaved(bookId, Guid.NewGuid(), 2, "editor");

        await bus.PublishAsync(sent);

        (await delivered.Task).ShouldBe(sent);
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Publish_WhenOtherBook_ShouldNotDeliver()
    {
        var bus = new InMemoryBookEvents(NullLogger<InMemoryBookEvents>.Instance);
        var mine = Guid.NewGuid();
        var received = new List<BookEvent>();
        var sawMine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe(mine, e =>
        {
            lock (received)
            {
                received.Add(e);
            }
            sawMine.TrySetResult();
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new StructureChanged(Guid.NewGuid(), "chapter"));
        var expected = new StructureChanged(mine, "scene");
        await bus.PublishAsync(expected);
        await sawMine.Task;

        lock (received)
        {
            received.ShouldBe([expected]);
        }
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Publish_WhenDisposed_ShouldStopDelivering()
    {
        var bus = new InMemoryBookEvents(NullLogger<InMemoryBookEvents>.Instance);
        var bookId = Guid.NewGuid();
        var disposedCount = 0;
        var liveSaw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = bus.Subscribe(bookId, _ =>
        {
            Interlocked.Increment(ref disposedCount);
            return Task.CompletedTask;
        });
        using var live = bus.Subscribe(bookId, _ =>
        {
            liveSaw.TrySetResult();
            return Task.CompletedTask;
        });
        disposed.Dispose();

        await bus.PublishAsync(new BookMetaChanged(bookId, ["title"]));
        await liveSaw.Task;

        disposedCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Publish_WhenHandlerBlocks_ShouldNotBlockPublisher()
    {
        var bus = new InMemoryBookEvents(NullLogger<InMemoryBookEvents>.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var slow = bus.Subscribe(null, _ => release.Task);

        await bus.PublishAsync(new StructureChanged(Guid.NewGuid(), "act"));

        release.Task.IsCompleted.ShouldBeFalse();
        release.SetResult();
    }
}
