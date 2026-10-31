using System.Threading.Channels;
using Ultron.Core.Memories;

namespace Ultron.Infrastructure.Memories;

/// <summary>
/// Channel: üreticiler (sohbet) iş atar, tüketici (işçi) sırayla alır. Bir postane kutusu gibi:
/// mektubu atan beklemez, postacı kendi hızında toplar.
/// </summary>
internal sealed class MemoryQueue : IMemoryQueue
{
    private readonly Channel<MemoryJob> _channel = Channel.CreateUnbounded<MemoryJob>(new() { SingleReader = true });
    private int _pending;

    public ChannelReader<MemoryJob> Reader => _channel.Reader;

    public void Enqueue(MemoryJob job)
    {
        Interlocked.Increment(ref _pending);
        if (!_channel.Writer.TryWrite(job))
            Interlocked.Decrement(ref _pending);
    }

    /// <summary>İşçi bir işi bitirince (başarılı ya da değil) çağırır.</summary>
    public void MarkDone() => Interlocked.Decrement(ref _pending);

    public async Task WaitUntilIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100, cancellationToken);
    }
}
