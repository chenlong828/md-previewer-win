using System.IO;
using System.IO.Pipes;
using System.Windows;
using MDPreview.Win.Settings;
using MDPreview.Win.Shell;

namespace MDPreview.Win;

public partial class App : Application
{
    const string MutexName = @"Local\MDPreview.SingleInstance";
    const string PipeName = "MDPreview.Ipc";

    readonly AppSettings _settings = AppSettings.Load();
    readonly List<DocumentWindow> _windows = [];

    Mutex? _instanceMutex;

    public static new App Current => (App)Application.Current;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("dispatcher exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Info($"start v{typeof(App).Assembly.GetName().Version} args=[{string.Join(' ', e.Args)}]");

        var path = e.Args.FirstOrDefault(arg => !arg.StartsWith('-'));

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            ForwardToPrimary(path);
            Shutdown(0);
            return;
        }

        StartPipeServer();
        OpenDocument(path);
    }

    public void OpenDocument(string? path)
    {
        var window = new DocumentWindow(_settings, path);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (_windows.Count == 0) Shutdown(0);
        };
        _windows.Add(window);
        window.Show();
    }

    public void ShutdownApp()
    {
        foreach (var window in _windows.ToList()) window.Close();
        if (_windows.Count == 0) Shutdown(0);
    }

    // ------------------------------------------------------------ 单实例转发

    static void ForwardToPrimary(string? path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path ?? "");
        }
        catch (Exception ex)
        {
            Log.Warn("single-instance forward failed: " + ex.Message);
        }
    }

    void StartPipeServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                        Dispatcher.Invoke(() => OpenDocument(line.Trim()));
                }
                catch (Exception ex)
                {
                    Log.Warn("ipc server error: " + ex.Message);
                    await Task.Delay(500);
                }
            }
        });
    }
}
