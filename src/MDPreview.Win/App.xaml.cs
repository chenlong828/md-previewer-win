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

        if (e.Args.Length > 1 && e.Args[0] == "--diag")
        {
            RunDiagnostics(e.Args.Skip(1).ToArray());
            return;
        }

        switch (e.Args.FirstOrDefault())
        {
            case "--install-association":
                RunAssociation(args => Io.FileAssociation.Install(), 0);
                return;
            case "--uninstall-association":
                RunAssociation(_ => Io.FileAssociation.Uninstall(), 0);
                return;
            case "--association-status":
                RunAssociation(_ => Io.FileAssociation.Status(), 0);
                return;
        }

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

    void RunAssociation(Func<string[], IReadOnlyList<string>> action, int successCode)
    {
        try
        {
            foreach (var line in action(Environment.GetCommandLineArgs())) Console.WriteLine(line);
            Shutdown(successCode);
        }
        catch (Exception ex)
        {
            Console.WriteLine("association failed: " + ex.Message);
            Log.Error("association failed", ex);
            Shutdown(1);
        }
    }

    // ---------------------------------------------------------------- 诊断子命令
    //
    //   MDPreview.exe --diag info
    //   MDPreview.exe --diag code <源文件>
    //   MDPreview.exe --diag export-html <源文件> <目标.html>
    //   MDPreview.exe --diag export-pdf  <源文件> <目标.pdf>
    //
    // 结果写 stdout（WinExe 无控制台，但重定向到文件可用），退出码 0=成功、1=失败。

    void RunDiagnostics(string[] args)
    {
        var sub = args.FirstOrDefault() ?? "info";
        if (sub == "info")
        {
            Console.WriteLine($"app        {typeof(App).Assembly.GetName().Version}");
            Console.WriteLine($"protocol   {AppInfo.ProtocolVersion}");
            Console.WriteLine($"base       {AppInfo.BaseDirectory}");
            Console.WriteLine($"userdata   {AppInfo.UserDataDirectory}");
            Console.WriteLine($"web        {AppInfo.WebDirectory} (kernel={AppInfo.HasKernel})");
            Console.WriteLine($"languages  {MDPreview.Win.Io.CodeLanguages.All.Count}");
            Console.WriteLine($"assoc      {(MDPreview.Win.Io.FileAssociation.IsInstalled ? "installed" : "not installed")}");
            Console.WriteLine($"log        {AppInfo.LogPath}");
            Shutdown(0);
            return;
        }

        var source = args.ElementAtOrDefault(1);
        var target = args.ElementAtOrDefault(2);
        if (source is null || !File.Exists(source))
        {
            Console.WriteLine($"missing source file: {source}");
            Shutdown(1);
            return;
        }

        _ = RunWindowDiagnosticsAsync(sub, source, target);
    }

    async Task RunWindowDiagnosticsAsync(string sub, string source, string? target)
    {
        var window = new DocumentWindow(_settings, source);
        var exitCode = 1;
        try
        {
            window.Show();
            await window.WaitReadyAsync();

            switch (sub)
            {
                case "code":
                    Console.WriteLine(await window.DiagCodeInfoAsync());
                    exitCode = 0;
                    break;
                case "state":
                    Console.WriteLine(await window.DiagStateAsync());
                    exitCode = 0;
                    break;
                case "export-html" when target is not null:
                    Console.WriteLine($"html bytes={await window.DiagExportHtmlAsync(target)} file={target}");
                    exitCode = 0;
                    break;
                case "export-pdf" when target is not null:
                    Console.WriteLine($"pdf ok={await window.DiagExportPdfAsync(target)} file={target}");
                    exitCode = 0;
                    break;
                default:
                    Console.WriteLine($"unknown diag command: {sub}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("diag failed: " + ex.Message);
            Log.Error("diag failed", ex);
        }
        finally
        {
            window.Close();
            Shutdown(exitCode);
        }
    }

    public void OpenDocument(string? path)    {
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
