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
    private static string _logPath = null!;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up crash log path early, before anything can fail
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaDownloader", "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "wpf-startup.log");

        // Wire up global exception handlers
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Log("Application starting");

        _mutex = new Mutex(true, MutexName, out var isNewInstance);

        if (!isNewInstance)
        {
            Log("Another instance already running, activating it");
            var hwnd = FindWindow(null, "Media Downloader");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            Shutdown();
            return;
        }

        Log("Single instance check passed, proceeding with startup");
        base.OnStartup(e);
        Log("OnStartup completed");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"FATAL dispatcher exception: {e.Exception}");
        MessageBox.Show(
            $"Media Downloader encountered an error and needs to close.\n\n{e.Exception.Message}\n\nDetails have been logged to:\n{_logPath}",
            "Media Downloader - Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log($"FATAL unhandled exception: {ex}");
        MessageBox.Show(
            $"Media Downloader encountered a fatal error.\n\n{ex?.Message}\n\nDetails have been logged to:\n{_logPath}",
            "Media Downloader - Fatal Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"Application exiting with code {e.ApplicationExitCode}");
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    internal static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(_logPath, line);
        }
        catch { /* logging must never throw */ }
    }
}
