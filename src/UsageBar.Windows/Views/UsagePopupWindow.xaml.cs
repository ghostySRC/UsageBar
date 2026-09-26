using System.Windows;
using UsageBar.Core.Models;
using UsageBar.Core.Services;
using UsageBar.Windows.Models;
using UsageBar.Windows.Services;

namespace UsageBar.Windows.Views;

public partial class UsagePopupWindow : Window
{
    private readonly UsageCoordinator _coordinator;
    private readonly Action<bool> _openSettings;
    private readonly Action<string> _launch;
    private readonly Action _exit;
    private bool _opening;

    public UsagePopupWindow(
        UsageCoordinator coordinator,
        Action<bool> openSettings,
        Action<string> launch,
        Action exit)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _openSettings = openSettings;
        _launch = launch;
        _exit = exit;
        ThemeManager.Apply();
    }

    public void ShowAt(System.Windows.Point pixelLocation)
    {
        DataContext = UsagePopupViewModel.Create(
            _coordinator.VisibleUsages,
            _coordinator.Settings,
            _coordinator.Running.CodexRunning,
            _coordinator.Running.AntigravityRunning);

        WindowStartupLocation = WindowStartupLocation.Manual;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pixelLocation.X, (int)pixelLocation.Y));
        var scale = TaskbarGeometry.GetScale(screen.DeviceName);
        Left = pixelLocation.X / scale.X;
        Top = pixelLocation.Y / scale.Y;
        if (!IsVisible) Show();
        else Activate();
        UpdateLayout();

        var work = screen.WorkingArea;
        var width = ActualWidth * scale.X;
        var height = ActualHeight * scale.Y;
        var leftPixels = Math.Clamp((int)pixelLocation.X - (int)width / 2, work.Left, Math.Max(work.Left, work.Right - (int)width));
        var topPixels = Math.Clamp((int)pixelLocation.Y - (int)height - 14, work.Top, Math.Max(work.Top, work.Bottom - (int)height));
        Left = leftPixels / scale.X;
        Top = topPixels / scale.Y;
        Activate();
        _opening = true;
        Dispatcher.BeginInvoke(() => _opening = false, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void RefreshData() => DataContext = UsagePopupViewModel.Create(
        _coordinator.VisibleUsages,
        _coordinator.Settings,
        _coordinator.Running.CodexRunning,
        _coordinator.Running.AntigravityRunning);

    private void OnDeactivated(object sender, EventArgs args)
    {
        if (!_opening) Close();
    }

    private void Close_Click(object sender, RoutedEventArgs args) => Close();

    private async void Refresh_Click(object sender, RoutedEventArgs args)
    {
        await _coordinator.RefreshAsync(bypassBackoff: true);
        RefreshData();
    }

    private void Settings_Click(object sender, RoutedEventArgs args) => _openSettings(false);
    private void LaunchCodex_Click(object sender, RoutedEventArgs args) => _launch("codex");
    private void LaunchAntigravity_Click(object sender, RoutedEventArgs args) => _launch("antigravity");
    private void Exit_Click(object sender, RoutedEventArgs args) => _exit();
}
