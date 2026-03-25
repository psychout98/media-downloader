using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MediaDownloader.Wpf.Services;

public class ServerManager : INotifyPropertyChanged, IDisposable
{
    private Process? _process;
    private bool _isRunning;
    private DateTime? _startTime;
    private readonly List<string> _logLines = new();
    private readonly object _logLock = new();
    private string _backendPath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? LogReceived;

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(Uptime)); }
    }

    public TimeSpan Uptime => _startTime.HasValue && IsRunning
        ? DateTime.Now - _startTime.Value
        : TimeSpan.Zero;

    public IReadOnlyList<string> LogLines
    {
        get { lock (_logLock) return _logLines.ToList(); }
    }

    public void Configure(string backendExePath)
    {
        _backendPath = backendExePath;
    }

    public void Start()
    {
        if (IsRunning || string.IsNullOrEmpty(_backendPath)) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _backendPath,
            WorkingDirectory = Path.GetDirectoryName(_backendPath) ?? ".",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutputReceived;
        _process.ErrorDataReceived += OnOutputReceived;
        _process.Exited += OnProcessExited;

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _startTime = DateTime.Now;
            IsRunning = true;
            AppendLog("Backend server started");
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to start backend: {ex.Message}");
            IsRunning = false;
        }
    }

    public void Stop()
    {
        if (!IsRunning || _process == null) return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch { /* best effort */ }
        finally
        {
            CleanupProcess();
            AppendLog("Backend server stopped");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _process?.ExitCode ?? -1;
        CleanupProcess();
        AppendLog($"Backend process exited with code {exitCode}");

        // Auto-restart on crash (non-zero exit)
        if (exitCode != 0)
        {
            AppendLog("Restarting backend after crash...");
            Task.Delay(2000).ContinueWith(_ => Start());
        }
    }

    private void CleanupProcess()
    {
        _process?.Dispose();
        _process = null;
        _startTime = null;
        IsRunning = false;
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) AppendLog(e.Data);
    }

    private void AppendLog(string line)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (_logLock)
        {
            _logLines.Add(timestamped);
            if (_logLines.Count > 1000) _logLines.RemoveAt(0);
        }
        LogReceived?.Invoke(timestamped);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
