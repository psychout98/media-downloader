using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MediaDownloader.Wpf;

public partial class App : System.Windows.Application
{
    private const string MutexName = "MediaDownloader_SingleInstance_Mutex";
    private Mutex? _mutex;
    private static string _logPath = "";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    private const int SW_RESTORE = 9;

    static App()
    {
        // This runs FIRST, before any XAML loads.
        // Write to a guaranteed-writable location.
        try
        {
            _logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediaDownloader", "logs", "wpf-startup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }
        catch
        {
            _logPath = Path.Combine(Path.GetTempPath(), "mediadownloader-startup.log");
        }

        Log("=== App starting ===");
        Log($"Log path: {_logPath}");
        Log($"Base dir: {AppDomain.CurrentDomain.BaseDirectory}");
        Log($"OS: {Environment.OSVersion}");
        Log($"64-bit: {Environment.Is64BitProcess}");

        // Install the earliest possible crash handler
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log($"FATAL AppDomain: {ex}");
            try
            {
                System.Windows.MessageBox.Show(
                    $"Startup crash:\n\n{ex?.GetType().Name}: {ex?.Message}\n\nInner: {ex?.InnerException?.GetType().Name}: {ex?.InnerException?.Message}\n\nLog: {_logPath}",
                    "Media Downloader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"Unobserved task: {args.Exception}");
            args.SetObserved();
        };

        // Single instance
        _mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            Log("Another instance running, activating it");
            var hwnd = FindWindow(null, "Media Downloader");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            Shutdown();
            return;
        }

        Log("Calling base.OnStartup (creates MainWindow)");
        base.OnStartup(e);
        Log("OnStartup done");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"FATAL dispatcher: {e.Exception}");
        System.Windows.MessageBox.Show(
            $"Error:\n\n{e.Exception.Message}\n\nInner: {e.Exception.InnerException?.Message}\n\nLog: {_logPath}",
            "Media Downloader",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"Exiting ({e.ApplicationExitCode})");
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
