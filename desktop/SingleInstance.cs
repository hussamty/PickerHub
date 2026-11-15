using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace PickerHub;

// The library lock and activation endpoint share one lifetime, before any window
// or global shortcut is created. A second launch never opens a second workspace.
internal sealed class SingleInstance : IDisposable
{
    readonly FileStream handle;
    readonly CancellationTokenSource stop = new();
    readonly string pipe;
    readonly Task listener;
    Action? activated;
    bool pending;
    readonly object gate = new();
    SingleInstance(FileStream handle, string pipe)
    { this.handle = handle; this.pipe = pipe; listener = Task.Run(Listen); }
    public static SingleInstance? Open()
    {
        Directory.CreateDirectory(Workspace.Folder);
        string pipe = "pickerhub-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Workspace.Folder))))[..24];
        try { return new(new FileStream(Path.Combine(Workspace.Folder, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), pipe); }
        catch (IOException)
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { client.Connect(2500); client.WriteByte(1); client.Flush(); return null; }
            catch (TimeoutException) { throw new IOException("PickerHub is already running. Close the previous version before opening this update."); }
        }
    }
    public void OnActivate(Action action)
    {
        lock (gate) { activated = action; if (pending) { pending = false; action(); } }
    }
    async Task Listen()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                byte[] message = new byte[1];
                if (await server.ReadAsync(message, stop.Token) == 1 && message[0] == 1)
                    lock (gate) { if (activated != null) activated(); else pending = true; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { SupportLog.Record("activation", e); }
    }
    public void Dispose() { stop.Cancel(); handle.Dispose(); }
}
