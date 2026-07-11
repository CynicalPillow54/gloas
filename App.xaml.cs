using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace GameDisplaySwitcher;

public partial class App : Application
{
    private const string MutexName = "GameDisplaySwitcher.Singleton.v1";
    private const string PipeName = "GameDisplaySwitcher.Commands.v1";
    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var command = e.Args.FirstOrDefault() ?? "--show";
        _mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            SendCommand(command);
            Shutdown();
            return;
        }

        _window = new MainWindow();
        MainWindow = _window;
        _pipeCts = new CancellationTokenSource();
        _ = ListenForCommands(_pipeCts.Token);
        _window.HandleCommand(command);
    }

    private async Task ListenForCommands(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(token);
                if (!string.IsNullOrWhiteSpace(command))
                    await Dispatcher.InvokeAsync(() => _window?.HandleCommand(command));
            }
            catch (OperationCanceledException) { }
            catch { await Task.Delay(250, token); }
        }
    }

    private static void SendCommand(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
