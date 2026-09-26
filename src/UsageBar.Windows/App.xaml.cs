using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using UsageBar.Core.Services;
using UsageBar.Windows.Services;
using UsageBar.Windows.Views;
using Forms = System.Windows.Forms;

namespace UsageBar.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private UsageCoordinator? _coordinator;
    private TrayController? _tray;
    private TaskbarWidgetWindow? _widget;
    private UsagePopupWindow? _popup;
    private SettingsWindow? _settings;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            _instanceMutex = new Mutex(initiallyOwned: true, $"Local\\UsageBar-{sid}", out _ownsMutex);
            if (!_ownsMutex)
            {
                Shutdown();
                return;
            }

            ThemeManager.Apply();
            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();
            if (args.Args.Contains("--enable-startup", StringComparer.OrdinalIgnoreCase))
            {
                settings = settings with { StartWithWindows = true };
                settingsStore.Save(settings);
            }
            else if (args.Args.Contains("--disable-startup", StringComparer.OrdinalIgnoreCase))
            {
                settings = settings with { StartWithWindows = false };
                settingsStore.Save(settings);
            }
            StartupManager.Apply(settings.StartWithWindows);
            _coordinator = new UsageCoordinator(settingsStore: settingsStore);
            _widget = new TaskbarWidgetWindow(_coordinator, OpenPopupFromWidget);
            _tray = new TrayController(_coordinator, OpenPopup, OpenSettings, ExitUsageBar);
            _coordinator.Changed += OnCoordinatorChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _coordinator.Start();
            UpdateSurfaces();
        }
        catch (Exception exception)
        {
            AppLog.Failure("startup", exception);
            Shutdown();
        }
    }

    private void OnCoordinatorChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(UpdateSurfaces), DispatcherPriority.Background);
            return;
        }
        UpdateSurfaces();
    }

    private void UpdateSurfaces()
    {
        _tray?.UpdateStatus();
        _widget?.RefreshState();
        _popup?.RefreshData();
    }

    private void OpenPopup(System.Drawing.Point pixelLocation)
    {
        if (_coordinator is null) return;
        _popup ??= CreatePopup();
        _popup.ShowAt(new System.Windows.Point(pixelLocation.X, pixelLocation.Y));
        _ = RefreshForPopupAsync();
    }

    private void OpenPopupFromWidget(System.Drawing.Point pixelLocation) => OpenPopup(pixelLocation);

    private UsagePopupWindow CreatePopup()
    {
        var popup = new UsagePopupWindow(
            _coordinator!,
            _ => OpenSettings(),
            provider => ApplicationLauncher.Launch(provider, _coordinator!.Running),
            ExitUsageBar);
        popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; };
        return popup;
    }

    private async Task RefreshForPopupAsync()
    {
        try { await _coordinator!.RefreshAsync(bypassBackoff: true); }
        catch (OperationCanceledException) { }
    }

    private void OpenSettings()
    {
        if (_coordinator is null) return;
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_coordinator, () => _widget?.ResetPosition());
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (args.IsAvailable && _coordinator is not null) _ = RefreshForPopupAsync();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume && _coordinator is not null)
        {
            _coordinator.NotifyLifecycleChanged();
            _ = RefreshForPopupAsync();
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs args)
    {
        if (args.Category == UserPreferenceCategory.General) _ = Dispatcher.BeginInvoke(new Action(ThemeManager.Apply));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs args)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_widget is { IsVisible: true }) _widget.RefreshState();
        }));
    }

    private void ExitUsageBar() => Shutdown();

    protected override void OnExit(ExitEventArgs args)
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_coordinator is not null)
        {
            _coordinator.Changed -= OnCoordinatorChanged;
            try { _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { }
        }
        _tray?.Dispose();
        _widget?.Close();
        _popup?.Close();
        _settings?.Close();
        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(args);
    }
}
