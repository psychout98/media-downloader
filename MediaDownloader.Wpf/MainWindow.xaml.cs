using System.ComponentModel;
using System.Windows.Forms;
using MediaDownloader.Wpf.ViewModels;
using WpfWindow = System.Windows.Window;
using WpfWindowState = System.Windows.WindowState;

namespace MediaDownloader.Wpf;

public partial class MainWindow : WpfWindow
{
    private MainViewModel? _viewModel;
    private NotifyIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        App.Log("MainWindow ctor: start");

        try
        {
            InitializeComponent();
            App.Log("MainWindow ctor: InitializeComponent done");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow ctor: InitializeComponent FAILED: {ex}");
            throw;
        }

        // Defer ALL heavy work to Loaded — guarantees the window appears first
        Loaded += OnLoaded;
        App.Log("MainWindow ctor: done");
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        App.Log("MainWindow.Loaded: start");

        try
        {
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            App.Log("MainWindow.Loaded: ViewModel bound");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow.Loaded: ViewModel creation FAILED: {ex}");
            // Window is already visible — show error in UI instead of crashing
            Title = "Media Downloader — Error";
            return;
        }

        try
        {
            SetupTrayIcon();
            App.Log("MainWindow.Loaded: tray icon done");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow.Loaded: tray icon failed (non-fatal): {ex.Message}");
        }

        try
        {
            await _viewModel.InitializeAsync();
            App.Log("MainWindow.Loaded: InitializeAsync done");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow.Loaded: InitializeAsync failed: {ex.Message}");
        }
    }

    private void DashboardTab_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.SelectedTab = 0;
    }

    private void SettingsTab_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.SelectedTab = 1;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "Media Downloader",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Open Web UI", null, (_, _) => _viewModel?.OpenWebUiCommand.Execute(null));
        menu.Items.Add("Launch MPC-BE", null, (_, _) => _viewModel?.LaunchMpcCommand.Execute(null));
        menu.Items.Add("Refresh Library", null, (_, _) => _viewModel?.RefreshLibraryCommand.Execute(null));
        menu.Items.Add("-");
        menu.Items.Add("Settings", null, (_, _) => { ShowFromTray(); if (_viewModel != null) _viewModel.SelectedTab = 1; });
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => { _forceClose = true; Close(); });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WpfWindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            try
            {
                _trayIcon?.ShowBalloonTip(2000, "Media Downloader",
                    "Minimized to system tray. Right-click for quick menu.", ToolTipIcon.Info);
            }
            catch { }
            return;
        }

        try { _viewModel?.Dispose(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
        base.OnClosing(e);
    }
}
