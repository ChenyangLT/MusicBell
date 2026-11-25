using System.IO.Pipes;
using System.Text;

namespace MusicBell.Core.Services;

/// <summary>单实例：第二个进程把参数丢给第一个进程然后退出。</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Local\\MusicBell.SingleInstance.v1";
    private const string PipeName = "MusicBell.Ipc.v1";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; private set; }

    /// <summary>收到别的实例发来的命令行参数。</summary>
    public event Action<string>? CommandReceived;

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        return createdNew;
    }

    /// <summary>作为第二个实例，把参数交给已经在跑的那个。</summary>
    public static bool SendToExisting(string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
            client.Connect(500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(payload);
            return true;
        }
        catch { return false; }
    }

    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line)) CommandReceived?.Invoke(line!);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { AppLog.Warn("IPC 异常：" + ex.Message); await Task.Delay(300).ConfigureAwait(false); }
            }
        }, token);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}
