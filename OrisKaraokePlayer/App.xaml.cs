using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OrisKaraokePlayerWpf;

public partial class App
{
    private const string MutexName = "ORIScore.Karaoke.Player.SingleInstance.v1";
    private const string PipeName = "ORIScore.Karaoke.Player.OpenFile.v1";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        string? fileToOpen = e.Args.Length > 0 && File.Exists(e.Args[0])
            ? Path.GetFullPath(e.Args[0])
            : null;

        if (!createdNew)
        {
            if (fileToOpen != null)
                await SendFileToExistingInstance(fileToOpen);

            Shutdown();
            return;
        }

        var win = new MainWindow();

        _pipeCts = new CancellationTokenSource();
        _ = ListenForOpenFileRequests(win, _pipeCts.Token);

        if (fileToOpen != null)
            win.LoadSongFile(fileToOpen, autoPlay: true);

        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task SendFileToExistingInstance(string path)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(1500);

            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(path);
        }
        catch
        {
            // Když běžící instance nestihne odpovědět, nebudeme otevírat druhé okno.
            // Asociace se tím nerozbije, jen se požadavek ignoruje.
        }
    }

    private static async Task ListenForOpenFileRequests(MainWindow win, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(token);

                using var reader = new StreamReader(pipe);
                string? path = await reader.ReadLineAsync();

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    await win.Dispatcher.InvokeAsync(() =>
                    {
                        // Background mode:
                        // Při otevření další .ock/.karaoke z Total Commanderu
                        // NEkrademe focus a NEtaháme okno dopředu.
                        // Jen zastavíme starou skladbu a pustíme novou.
                        win.LoadSongFile(path, autoPlay: true);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Server trubky po chybě zkusí běžet dál.
            }
        }
    }
}
