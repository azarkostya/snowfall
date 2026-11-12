using System.IO.Pipes;
using System.Text;

namespace Snowfall;

/// <summary>
/// Named pipe <c>\\.\pipe\snowfall</c>. A client writes one command line
/// (<c>status | toggle | on | off | shake</c>) and gets one line of JSON back.
/// Used by <c>Snowfall.exe --toggle</c> (desktop shortcut) and by CalWall.
/// </summary>
internal sealed class CommandServer : IDisposable
{
    public const string PipeName = "snowfall";

    private readonly Func<string, string> _handle;
    private readonly CancellationTokenSource _cts = new();

    /// <param name="handle">Called on a pool thread; must marshal to the UI thread itself.</param>
    public CommandServer(Func<string, string> handle)
    {
        _handle = handle;
        Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                // Serve on its own task so the next listening instance exists right away.
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => Serve(connected));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(timeout.Token) ?? "";
                var reply = _handle(line.Trim().ToLowerInvariant());
                var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                await pipe.WriteAsync(bytes, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
                pipe.WaitForPipeDrain();
            }
            catch
            {
                // Client went away or sent garbage; nothing to do.
            }
        }
    }

    /// <summary>Client side: send a command to the running instance. Returns null if none is running.</summary>
    public static string? Send(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1500);
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            pipe.Write(bytes);
            pipe.Flush();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            return reader.ReadLine();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _cts.Cancel();
}
