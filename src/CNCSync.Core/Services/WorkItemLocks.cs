namespace CNCSync.Core.Services;

internal sealed class WorkItemLocks
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken token)
    {
        Entry entry;
        lock (_gate)
        {
            entry = _entries.GetValueOrDefault(key) ?? new Entry(new SemaphoreSlim(1), 0);
            _entries[key] = entry with { Users = entry.Users + 1 };
        }

        try
        {
            await entry.Lock.WaitAsync(token);
            return new Lease(this, key, entry.Lock);
        }
        catch
        {
            RemoveUser(key);
            throw;
        }
    }

    private void RemoveUser(string key)
    {
        lock (_gate)
        {
            var entry = _entries[key];
            if (entry.Users == 1)
            {
                _entries.Remove(key);
                entry.Lock.Dispose();
            }
            else
            {
                _entries[key] = entry with { Users = entry.Users - 1 };
            }
        }
    }

    private sealed record Entry(SemaphoreSlim Lock, int Users);

    private sealed class Lease(WorkItemLocks owner, string key, SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose()
        {
            semaphore.Release();
            owner.RemoveUser(key);
        }
    }
}
