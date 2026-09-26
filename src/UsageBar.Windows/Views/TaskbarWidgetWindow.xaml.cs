using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using UsageBar.Core.Models;
using UsageBar.Core.Services;
using UsageBar.Windows.Models;
using UsageBar.Windows.Services;
using Forms = System.Windows.Forms;

namespace UsageBar.Windows.Views;

public partial class TaskbarWidgetWindow : Window
{
    private readonly UsageCoordinator _coordinator;
    private readonly Action<System.Drawing.Point> _openPopup;
    private bool _suppressClick;
    private bool _hasWindowStyle;

    public TaskbarWidgetWindow(UsageCoordinator coordinator, Action<System.Drawing.Point> openPopup)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _openPopup = openPopup;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => PositionAtTaskbar();
    }

    public void RefreshState()
    {
        var settings = _coordinator.Settings;
        DataContext = UsagePopupViewModel.CreateWidgetItems(_coordinator.VisibleUsages, settings);
        Topmost = settings.AlwaysOnTop;
        if (!_coordinator.ShouldShowWidget)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        UpdateLayout();
        if (!_hasWindowStyle) ApplyWindowStyle();
        PositionAtTaskbar();
    }

    public void ResetPosition()
    {
        _coordinator.SaveSettings(_coordinator.Settings with { WidgetLeft = null, WidgetTop = null, WidgetMonitor = null });
        PositionAtTaskbar(forceDefault: true);
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => ApplyWindowStyle();

    private void ApplyWindowStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        TaskbarGeometry.ApplyWindowStyle(handle);
        _hasWindowStyle = true;
    }

    private void PositionAtTaskbar(bool forceDefault = false)
    {
        if (!IsLoaded || !IsVisible) return;
        UpdateLayout();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var settings = _coordinator.Settings;
        var left = forceDefault ? null : settings.WidgetLeft;
        var top = forceDefault ? null : settings.WidgetTop;
        var monitor = forceDefault ? null : settings.WidgetMonitor;
        var position = TaskbarGeometry.FindPosition(ActualWidth, ActualHeight, left, top, monitor);
        TaskbarGeometry.MoveWithoutActivation(handle, position, ActualWidth, ActualHeight, settings.AlwaysOnTop);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (FindNamedAncestor(args.OriginalSource as DependencyObject, "DragHandle") is null) return;
        args.Handled = true;
        _suppressClick = true;
        try
        {
            DragMove();
            var handle = new WindowInteropHelper(this).Handle;
            var snapped = TaskbarGeometry.Snap(handle, ActualWidth, ActualHeight);
            TaskbarGeometry.MoveWithoutActivation(handle, snapped, ActualWidth, ActualHeight, _coordinator.Settings.AlwaysOnTop);
            var saved = TaskbarGeometry.GetCurrentPosition(handle);
            _coordinator.SaveSettings(_coordinator.Settings with
            {
                WidgetLeft = saved.Left,
                WidgetTop = saved.Top,
                WidgetMonitor = saved.Monitor
            });
        }
        catch { }
        finally
        {
            Dispatcher.BeginInvoke(() => _suppressClick = false, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (_suppressClick) return;
        var point = Forms.Cursor.Position;
        _openPopup(point);
    }

    private static DependencyObject? FindNamedAncestor(DependencyObject? source, string name)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.Name == name) return element;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
