using System.Text.Json;
using System.Threading.Channels;

namespace PickerHub;

// Local, opt-in, bounded and off the UI thread. Never include exception messages,
// paths, colors or screenshots. Export is explicit and contains this session only.
internal static class SupportLog
{
    internal static volatile bool Enabled;
    static readonly object gate = new();
    static readonly Queue<Entry> recent = new();
    internal sealed record Entry(DateTimeOffset Time, string Version, string Operation, string? Exception, double? Milliseconds);
    static readonly Channel<Entry> pending = Channel.CreateBounded<Entry>(new BoundedChannelOptions(64)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    static readonly Task worker = Task.Run(Write);
    public static long Measure() => Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

    public static void Measure(string operation, long start)
    {
        if (start != 0)
            Record(operation, milliseconds: System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    public static void Record(string operation, Exception? error = null, double? milliseconds = null)
    {
        if (!Enabled) return;
        var entry = new Entry(DateTimeOffset.UtcNow, App.Version, operation, error?.GetType().Name, milliseconds);
        lock (gate) { recent.Enqueue(entry); if (recent.Count > 64) recent.Dequeue(); }
        pending.Writer.TryWrite(entry);
    }
    public static Entry[] Snapshot() { lock (gate) return Enabled ? recent.ToArray() : []; }
    static async Task Write()
    {
        await foreach (var entry in pending.Reader.ReadAllAsync())
        {
            if (!Enabled) continue;
            try
            {
                var path = Path.Combine(Workspace.Folder, "diagnostics.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".previous", true);
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch { /* Diagnostics must never interrupt picking or saving. */ }
        }
    }
}
