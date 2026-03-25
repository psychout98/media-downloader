using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using MediaDownloader.Wpf.Services;

namespace MediaDownloader.Wpf.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ServerManager _serverManager;
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _pollTimer;

    // Tab state
    private int _selectedTab;
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }

    // Server status
    private string _serverStatus = "Stopped";
    public string ServerStatus { get => _serverStatus; set => SetProperty(ref _serverStatus, value); }

    private string _serverSubtitle = "";
    public string ServerSubtitle { get => _serverSubtitle; set => SetProperty(ref _serverSubtitle, value); }

    private string _statusDotColor = "Red";
    public string StatusDotColor { get => _statusDotColor; set => SetProperty(ref _statusDotColor, value); }

    public bool IsServerRunning => _serverManager.IsRunning;

    // Info grid
    private int _activeJobCount;
    public int ActiveJobCount { get => _activeJobCount; set => SetProperty(ref _activeJobCount, value); }

    private int _libraryCount;
    public int LibraryCount { get => _libraryCount; set => SetProperty(ref _libraryCount, value); }

    private string _mpcStatus = "Unknown";
    public string MpcStatus { get => _mpcStatus; set => SetProperty(ref _mpcStatus, value); }

    private string _diskFree = "—";
    public string DiskFree { get => _diskFree; set => SetProperty(ref _diskFree, value); }

    // Active downloads
    public ObservableCollection<DownloadItemVm> ActiveDownloads { get; } = new();

    // Version
    private string _currentVersion = "v0.1.0";
    public string CurrentVersion { get => _currentVersion; set => SetProperty(ref _currentVersion, value); }

    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; set => SetProperty(ref _updateAvailable, value); }

    private string _latestVersion = "";
    public string LatestVersion { get => _latestVersion; set => SetProperty(ref _latestVersion, value); }

    private string? _releaseUrl;

    // Settings
    private string _moviesDir = "";
    public string MoviesDir { get => _moviesDir; set => SetProperty(ref _moviesDir, value); }

    private string _tvDir = "";
    public string TvDir { get => _tvDir; set => SetProperty(ref _tvDir, value); }

    private string _archiveDir = "";
    public string ArchiveDir { get => _archiveDir; set => SetProperty(ref _archiveDir, value); }

    private string _appDataDir = "";
    public string AppDataDir { get => _appDataDir; set => SetProperty(ref _appDataDir, value); }

    private string _tmdbApiKey = "";
    public string TmdbApiKey { get => _tmdbApiKey; set => SetProperty(ref _tmdbApiKey, value); }

    private string _rdApiKey = "";
    public string RdApiKey { get => _rdApiKey; set => SetProperty(ref _rdApiKey, value); }

    private string _rdKeyStatus = "";
    public string RdKeyStatus { get => _rdKeyStatus; set => SetProperty(ref _rdKeyStatus, value); }

    private string _mpcUrl = "http://127.0.0.1:13579";
    public string MpcUrl { get => _mpcUrl; set => SetProperty(ref _mpcUrl, value); }

    private string _mpcExePath = "";
    public string MpcExePath { get => _mpcExePath; set => SetProperty(ref _mpcExePath, value); }

    private int _maxDownloads = 2;
    public int MaxDownloads { get => _maxDownloads; set => SetProperty(ref _maxDownloads, value); }

    private double _watchThreshold = 0.85;
    public double WatchThreshold { get => _watchThreshold; set => SetProperty(ref _watchThreshold, value); }

    private bool _startOnBoot;
    public bool StartOnBoot { get => _startOnBoot; set => SetProperty(ref _startOnBoot, value); }

    private bool _autoStartServer = true;
    public bool AutoStartServer { get => _autoStartServer; set => SetProperty(ref _autoStartServer, value); }

    private bool _autoUpdate;
    public bool AutoUpdate { get => _autoUpdate; set => SetProperty(ref _autoUpdate, value); }

    private int _port = 8000;
    public int Port { get => _port; set => SetProperty(ref _port, value); }

    // Commands
    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand OpenWebUiCommand { get; }
    public ICommand OpenMediaLibraryCommand { get; }
    public ICommand LaunchMpcCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand CancelSettingsCommand { get; }
    public ICommand UpdateNowCommand { get; }
    public ICommand TestRdKeyCommand { get; }

    public MainViewModel()
    {
        _serverManager = new ServerManager();
        _apiClient = new ApiClient();

        // Determine backend path
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var backendExe = Path.Combine(appDir, "backend", "MediaDownloader.Api.exe");
        if (File.Exists(backendExe))
            _serverManager.Configure(backendExe);

        _serverManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerManager.IsRunning))
            {
                OnPropertyChanged(nameof(IsServerRunning));
                UpdateServerStatus();
            }
        };

        // Commands
        StartServerCommand = new RelayCommand(StartServer, () => !IsServerRunning);
        StopServerCommand = new RelayCommand(StopServer, () => IsServerRunning);
        OpenWebUiCommand = new RelayCommand(() => OpenUrl($"http://localhost:{Port}"));
        OpenMediaLibraryCommand = new RelayCommand(() => OpenFolder(MoviesDir));
        LaunchMpcCommand = new RelayCommand(LaunchMpc);
        RefreshLibraryCommand = new RelayCommand(async () => await RefreshLibraryAsync());
        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
        CancelSettingsCommand = new RelayCommand(async () => await LoadSettingsAsync());
        UpdateNowCommand = new RelayCommand(async () => await UpdateNowAsync());
        TestRdKeyCommand = new RelayCommand(async () => await TestRdKeyAsync());

        // Poll timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) => await PollAsync();

        UpdateServerStatus();
    }

    public async Task InitializeAsync()
    {
        if (AutoStartServer)
            StartServer();

        await Task.Delay(2000); // Wait for backend to start
        await LoadSettingsAsync();
        await CheckVersionAsync();
        _pollTimer.Start();
    }

    private void StartServer()
    {
        _serverManager.Start();
        _apiClient.UpdatePort(Port);
        _pollTimer.Start();
    }

    private void StopServer()
    {
        _pollTimer.Stop();
        _serverManager.Stop();
    }

    private void UpdateServerStatus()
    {
        if (IsServerRunning)
        {
            ServerStatus = "Running";
            var uptime = _serverManager.Uptime;
            ServerSubtitle = $"http://localhost:{Port} · Uptime {FormatUptime(uptime)}";
            StatusDotColor = "Green";
        }
        else
        {
            ServerStatus = "Stopped";
            ServerSubtitle = "";
            StatusDotColor = "Red";
        }
    }

    private async Task PollAsync()
    {
        if (!IsServerRunning) return;

        try
        {
            UpdateServerStatus();

            // Poll jobs
            var jobs = await _apiClient.GetJobsAsync();
            if (jobs?.Jobs != null)
            {
                var active = jobs.Jobs.Where(j => j.Status is "pending" or "searching" or "found" or "adding_to_rd" or "waiting_for_rd" or "downloading" or "organizing").ToArray();
                ActiveJobCount = active.Length;

                ActiveDownloads.Clear();
                foreach (var job in active)
                {
                    ActiveDownloads.Add(new DownloadItemVm
                    {
                        Name = job.TorrentName ?? job.Query ?? "Unknown",
                        Status = job.Status,
                        Progress = job.Progress,
                        SizeBytes = job.SizeBytes,
                        DownloadedBytes = job.DownloadedBytes
                    });
                }
            }

            // Poll library count
            var library = await _apiClient.GetLibraryAsync();
            if (library != null)
                LibraryCount = library.Count;

            // Check MPC
            try
            {
                var status = await _apiClient.GetAsync<Dictionary<string, object>>("mpc/status");
                MpcStatus = status != null && status.ContainsKey("reachable") && status["reachable"]?.ToString() == "True"
                    ? "Connected" : "Disconnected";
            }
            catch { MpcStatus = "Disconnected"; }

            // Disk free
            UpdateDiskFree();
        }
        catch { /* polling failure is non-fatal */ }
    }

    private void UpdateDiskFree()
    {
        try
        {
            var dir = !string.IsNullOrEmpty(MoviesDir) ? MoviesDir : "C:\\";
            var root = Path.GetPathRoot(dir);
            if (root != null)
            {
                var drive = new DriveInfo(root);
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                DiskFree = $"{freeGb:F0} GB";
            }
        }
        catch { DiskFree = "—"; }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _apiClient.GetSettingsAsync();
            if (settings == null) return;

            MoviesDir = settings.GetValueOrDefault("MOVIES_DIR") ?? "";
            TvDir = settings.GetValueOrDefault("TV_DIR") ?? "";
            ArchiveDir = settings.GetValueOrDefault("ARCHIVE_DIR") ?? "";
            AppDataDir = settings.GetValueOrDefault("APP_DATA_DIR") ?? "";
            TmdbApiKey = settings.GetValueOrDefault("TMDB_API_KEY") ?? "";
            RdApiKey = settings.GetValueOrDefault("REAL_DEBRID_API_KEY") ?? "";
            MpcUrl = settings.GetValueOrDefault("MPC_BE_URL") ?? "http://127.0.0.1:13579";
            MpcExePath = settings.GetValueOrDefault("MPC_BE_EXE") ?? "";
            Port = int.TryParse(settings.GetValueOrDefault("PORT"), out var p) ? p : 8000;
            MaxDownloads = int.TryParse(settings.GetValueOrDefault("MAX_CONCURRENT_DOWNLOADS"), out var m) ? m : 2;
            WatchThreshold = double.TryParse(settings.GetValueOrDefault("WATCH_THRESHOLD"), out var w) ? w : 0.85;
        }
        catch { /* settings load failure is non-fatal */ }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new Dictionary<string, string>
            {
                ["MOVIES_DIR"] = MoviesDir,
                ["TV_DIR"] = TvDir,
                ["ARCHIVE_DIR"] = ArchiveDir,
                ["APP_DATA_DIR"] = AppDataDir,
                ["TMDB_API_KEY"] = TmdbApiKey,
                ["REAL_DEBRID_API_KEY"] = RdApiKey,
                ["MPC_BE_URL"] = MpcUrl,
                ["MPC_BE_EXE"] = MpcExePath,
                ["PORT"] = Port.ToString(),
                ["MAX_CONCURRENT_DOWNLOADS"] = MaxDownloads.ToString(),
                ["WATCH_THRESHOLD"] = WatchThreshold.ToString("F2")
            };
            await _apiClient.PostSettingsAsync(settings);
        }
        catch { /* save failure handled by UI */ }
    }

    private async Task TestRdKeyAsync()
    {
        try
        {
            var result = await _apiClient.GetAsync<Dictionary<string, object>>("settings/test-rd");
            if (result != null && result.ContainsKey("ok") && result["ok"]?.ToString() == "True")
                RdKeyStatus = "VALID";
            else
                RdKeyStatus = "INVALID";
        }
        catch { RdKeyStatus = "ERROR"; }
    }

    private async Task CheckVersionAsync()
    {
        try
        {
            var version = await _apiClient.GetVersionAsync();
            if (version == null) return;
            CurrentVersion = $"v{version.Version}";
            UpdateAvailable = version.UpdateAvailable;
            LatestVersion = version.LatestVersion ?? "";
            _releaseUrl = version.ReleaseUrl;
        }
        catch { /* non-fatal */ }
    }

    private async Task UpdateNowAsync()
    {
        if (string.IsNullOrEmpty(_releaseUrl)) return;
        // Download installer and run silent update
        // For now, open the release page
        OpenUrl(_releaseUrl);
        await Task.CompletedTask;
    }

    private async Task RefreshLibraryAsync()
    {
        try { await _apiClient.PostRefreshLibraryAsync(); }
        catch { /* non-fatal */ }
    }

    private void LaunchMpc()
    {
        try
        {
            if (!string.IsNullOrEmpty(MpcExePath) && File.Exists(MpcExePath))
                Process.Start(new ProcessStartInfo(MpcExePath) { UseShellExecute = true });
        }
        catch { /* non-fatal */ }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* non-fatal */ }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch { /* non-fatal */ }
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m";
        return $"{ts.Seconds}s";
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _serverManager.Dispose();
        _apiClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class DownloadItemVm : ViewModelBase
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public long SizeBytes { get; set; }
    public long DownloadedBytes { get; set; }

    public string ProgressText => Status == "pending"
        ? "Queued"
        : $"{Progress * 100:F0}%";

    public string SizeText
    {
        get
        {
            if (SizeBytes <= 0) return "";
            var totalMb = SizeBytes / (1024.0 * 1024);
            var dlMb = DownloadedBytes / (1024.0 * 1024);
            return totalMb >= 1024
                ? $"{dlMb / 1024:F1} / {totalMb / 1024:F1} GB"
                : $"{dlMb:F0} / {totalMb:F0} MB";
        }
    }
}
