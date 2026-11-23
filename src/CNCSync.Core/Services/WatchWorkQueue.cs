using System.Threading.Channels;

namespace CNCSync.Core.Services;

internal sealed class WatchWorkQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Channel<string> _ready = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _tasks;

    public WatchWorkQueue(
        int concurrency,
        TimeProvider clock,
        Func<WorkItemReadyEvent, CancellationToken, Task<DateTimeOffset?>> process)
    {
        _tasks = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => ConsumeAsync(process, _stop.Token)))
            .Append(Task.Run(() => ScheduleRetriesAsync(clock, _stop.Token)))
            .ToArray();
    }

    public void Submit(WorkItemReadyEvent item)
    {
        var key = WorkItemKey.Create(item.Profile, item.Path);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            if (!_slots.TryGetValue(key, out var slot))
            {
                _slots.Add(key, new Slot(item));
                _ready.Writer.TryWrite(key);
            }
            else
            {
                _slots[key] = slot with { Latest = item, RetryAt = null };
                if (!slot.Active && slot.RetryAt is not null)
                {
                    _ready.Writer.TryWrite(key);
                }
            }
        }
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await Task.WhenAll(_tasks);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task ConsumeAsync(
        Func<WorkItemReadyEvent, CancellationToken, Task<DateTimeOffset?>> process, CancellationToken token)
    {
        await foreach (var key in _ready.Reader.ReadAllAsync(token))
        {
            token.ThrowIfCancellationRequested();
            WorkItemReadyEvent item;
            lock (_gate)
            {
                var slot = _slots[key];
                item = slot.Latest;
                _slots[key] = slot with { Active = true };
            }

            var retryAt = await process(item, token);
            lock (_gate)
            {
                var slot = _slots[key];
                if (!ReferenceEquals(item, slot.Latest))
                {
                    _slots[key] = slot with { Active = false };
                    _ready.Writer.TryWrite(key);
                }
                else if (retryAt is not null)
                {
                    _slots[key] = slot with { Active = false, RetryAt = retryAt };
                }
                else
                {
                    _slots.Remove(key);
                }
            }
        }
    }

    private async Task ScheduleRetriesAsync(TimeProvider clock, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        while (await timer.WaitForNextTickAsync(token))
        {
            lock (_gate)
            {
                var now = clock.GetUtcNow();
                foreach (var (key, slot) in _slots.Where(pair => pair.Value.RetryAt <= now).ToArray())
                {
                    _slots[key] = slot with { RetryAt = null };
                    _ready.Writer.TryWrite(key);
                }
            }
        }
    }

    private sealed record Slot(WorkItemReadyEvent Latest, bool Active = false, DateTimeOffset? RetryAt = null);
}
