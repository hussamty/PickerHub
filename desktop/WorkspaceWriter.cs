namespace PickerHub;

// One bounded, serial writer. The UI owns mutable state; this worker receives only
// immutable snapshots. Bursts coalesce, and FlushAsync is the durability boundary.
internal sealed class WorkspaceWriter(Action<string>? save = null)
{
    readonly object gate = new();
    readonly Action<string> save = save ?? Workspace.SaveSnapshot;
    Task worker = Task.CompletedTask;
    string? pending, latest;
    Exception? failure;
    bool running;
    public event Action<Exception>? Failed;

    public void Enqueue(string snapshot)
    {
        lock (gate)
        {
            latest = pending = snapshot; failure = null;
            if (running) return;
            running = true; worker = Task.Run(Drain);
        }
    }
    async Task Drain()
    {
        // Collect slider/keyboard bursts without doing synchronous disk work on input.
        await Task.Delay(80).ConfigureAwait(false);
        while (true)
        {
            string snapshot;
            lock (gate)
            {
                if (pending == null) { running = false; return; }
                snapshot = pending; pending = null;
            }
            try { save(snapshot); }
            catch (Exception e)
            {
                lock (gate) { failure = e; running = false; }
                Failed?.Invoke(e); return;
            }
        }
    }
    public async Task FlushAsync()
    {
        while (true)
        {
            Task current;
            lock (gate) current = worker;
            await current.ConfigureAwait(false);
            lock (gate)
            {
                if (running) continue;
                if (failure != null) throw new IOException("Could not save the library. Your changes are still open; retry saving before closing.", failure);
                return;
            }
        }
    }
    public void Retry()
    {
        lock (gate) { if (latest != null) Enqueue(latest); }
    }
}
