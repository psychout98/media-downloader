using System.ComponentModel;
using System.Windows.Forms;
using MediaDownloader.Wpf.ViewModels;
using WpfWindow = System.Windows.Window;
using WpfWindowState = System.Windows.WindowState;
using RoutedEventArgs = System.Windows.RoutedEventArgs;

namespace MediaDownloader.Wpf;

public partial class MainWindow : WpfWindow
{
    private readonly MainViewModel _viewModel;
    private NotifyIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        SetupTrayIcon();
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void DashboardTab_Checked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedTab = 0;
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedTab = 1;
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
        menu.Items.Add("Open Web UI", null, (_, _) => _viewModel.OpenWebUiCommand.Execute(null));
        menu.Items.Add("Launch MPC-BE", null, (_, _) => _viewModel.LaunchMpcCommand.Execute(null));
        menu.Items.Add("Refresh Library", null, (_, _) => _viewModel.RefreshLibraryCommand.Execute(null));
        menu.Items.Add("-");
        menu.Items.Add("Settings", null, (_, _) => { ShowFromTray(); _viewModel.SelectedTab = 1; });
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
            _trayIcon?.ShowBalloonTip(2000, "Media Downloader",
                "Minimized to system tray. Right-click for quick menu.", ToolTipIcon.Info);
            return;
        }

        _viewModel.Dispose();
        _trayIcon?.Dispose();
        base.OnClosing(e);
    }
}
