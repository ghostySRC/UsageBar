using Forms = System.Windows.Forms;
using System.Runtime.InteropServices;

namespace UsageBar.Windows.Services;

public enum DockEdge { Bottom, Top, Left, Right }

public sealed record WidgetPosition(int Left, int Top, string Monitor);

public static class TaskbarGeometry
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwcpRound = 2;
    private const int MonitorDefaultToNearest = 2;

    public static WidgetPosition FindPosition(double widthDip, double heightDip, double? savedLeft, double? savedTop, string? savedMonitor)
    {
        var screen = ChooseScreen(savedLeft, savedTop, savedMonitor);
        var dpi = GetDpi(screen);
        var width = Math.Max(1, (int)Math.Round(widthDip * dpi.X / 96));
        var height = Math.Max(1, (int)Math.Round(heightDip * dpi.Y / 96));
        var work = screen.WorkingArea;
        var edge = GetTaskbarEdge(screen);
        var left = savedLeft is not null && string.Equals(savedMonitor, screen.DeviceName, StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(savedLeft.Value)
            : work.Left + (edge is DockEdge.Left or DockEdge.Right ? 0 : (int)Math.Round(12d * dpi.X / 96d));
        var top = savedTop is not null && string.Equals(savedMonitor, screen.DeviceName, StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(savedTop.Value)
            : DefaultTop(work, height, edge, dpi.Y);

        if (savedLeft is null || savedTop is null || !string.Equals(savedMonitor, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            switch (edge)
            {
                case DockEdge.Top: top = work.Top; break;
                case DockEdge.Left: left = work.Left; break;
                case DockEdge.Right: left = work.Right - width; break;
            }
        }

        return Clamp(new WidgetPosition(left, top, screen.DeviceName), screen, width, height, dpi.X, dpi.Y);
    }

    public static WidgetPosition Snap(IntPtr window, double widthDip, double heightDip)
    {
        var bounds = NativeMethods.GetWindowRectangle(window);
        var screen = Forms.Screen.FromRectangle(bounds);
        var dpi = GetDpi(screen);
        var width = Math.Max(1, (int)Math.Round(widthDip * dpi.X / 96));
        var height = Math.Max(1, (int)Math.Round(heightDip * dpi.Y / 96));
        var work = screen.WorkingArea;
        var edge = GetTaskbarEdge(screen);
        var left = bounds.Left;
        var top = bounds.Top;
        switch (edge)
        {
            case DockEdge.Bottom:
                top = work.Bottom - height;
                break;
            case DockEdge.Top:
                top = work.Top;
                break;
            case DockEdge.Left:
                left = work.Left;
                break;
            case DockEdge.Right:
                left = work.Right - width;
                break;
        }
        return Clamp(new WidgetPosition(left, top, screen.DeviceName), screen, width, height, dpi.X, dpi.Y);
    }

    public static void ApplyWindowStyle(IntPtr window)
    {
        var style = NativeMethods.GetWindowLongPtr(window, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        NativeMethods.SetWindowLongPtr(window, GwlExStyle, new IntPtr(style));
        var preference = DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        var darkMode = ThemeManager.IsDarkMode() ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }

    public static void MoveWithoutActivation(IntPtr window, WidgetPosition position, double widthDip, double heightDip, bool topmost)
    {
        var screen = ChooseScreen(position.Left, position.Top, position.Monitor);
        var dpi = GetDpi(screen);
        var width = Math.Max(1, (int)Math.Round(widthDip * dpi.X / 96));
        var height = Math.Max(1, (int)Math.Round(heightDip * dpi.Y / 96));
        var insertAfter = topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndTop;
        NativeMethods.SetWindowPos(window, insertAfter, position.Left, position.Top, width, height, SwpNoActivate);
    }

    public static WidgetPosition GetCurrentPosition(IntPtr window)
    {
        var rectangle = NativeMethods.GetWindowRectangle(window);
        var screen = Forms.Screen.FromRectangle(rectangle);
        return new WidgetPosition(rectangle.Left, rectangle.Top, screen.DeviceName);
    }

    public static (double X, double Y) GetScale(string monitor)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName.Equals(monitor, StringComparison.OrdinalIgnoreCase))
            ?? Forms.Screen.PrimaryScreen
            ?? Forms.Screen.AllScreens.First();
        var dpi = GetDpi(screen);
        return (dpi.X / 96d, dpi.Y / 96d);
    }

    public static void RefreshStyle(IntPtr window) => ApplyWindowStyle(window);

    private static int DefaultTop(System.Drawing.Rectangle work, int height, DockEdge edge, uint dpiY) => edge switch
    {
        DockEdge.Bottom => work.Bottom - height,
        DockEdge.Top => work.Top,
        _ => work.Top + (int)Math.Round(12d * dpiY / 96d)
    };

    private static WidgetPosition Clamp(WidgetPosition position, Forms.Screen screen, int width, int height, uint dpiX, uint dpiY)
    {
        var work = screen.WorkingArea;
        var marginX = (int)Math.Round(4d * dpiX / 96d);
        var marginY = (int)Math.Round(4d * dpiY / 96d);
        var maxLeft = Math.Max(work.Left, work.Right - width);
        var maxTop = Math.Max(work.Top, work.Bottom - height);
        return position with
        {
            Left = Math.Clamp(position.Left, work.Left + Math.Min(marginX, Math.Max(0, maxLeft - work.Left)), maxLeft),
            Top = Math.Clamp(position.Top, work.Top + Math.Min(marginY, Math.Max(0, maxTop - work.Top)), maxTop),
            Monitor = screen.DeviceName
        };
    }

    private static Forms.Screen ChooseScreen(double? left, double? top, string? name)
    {
        var screens = Forms.Screen.AllScreens;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var exact = screens.FirstOrDefault(screen => screen.DeviceName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }
        if (left is not null && top is not null)
        {
            var point = new System.Drawing.Point((int)Math.Round(left.Value), (int)Math.Round(top.Value));
            var containing = screens.FirstOrDefault(screen => screen.Bounds.Contains(point));
            if (containing is not null) return containing;
        }
        return Forms.Screen.PrimaryScreen ?? screens.First();
    }

    private static DockEdge GetTaskbarEdge(Forms.Screen screen)
    {
        var handle = FindTaskbar(screen.DeviceName);
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var taskbar)) return DockEdge.Bottom;
        var bounds = screen.Bounds;
        if (taskbar.Bottom >= bounds.Bottom - 2) return DockEdge.Bottom;
        if (taskbar.Top <= bounds.Top + 2) return DockEdge.Top;
        if (taskbar.Left <= bounds.Left + 2) return DockEdge.Left;
        if (taskbar.Right >= bounds.Right - 2) return DockEdge.Right;
        return DockEdge.Bottom;
    }

    private static IntPtr FindTaskbar(string monitor)
    {
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && Forms.Screen.FromHandle(primary).DeviceName.Equals(monitor, StringComparison.OrdinalIgnoreCase)) return primary;
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            var className = NativeMethods.GetClassName(handle);
            if (!className.Equals("Shell_SecondaryTrayWnd", StringComparison.Ordinal)) return true;
            if (Forms.Screen.FromHandle(handle).DeviceName.Equals(monitor, StringComparison.OrdinalIgnoreCase))
            {
                found = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static (uint X, uint Y) GetDpi(Forms.Screen screen)
    {
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1), MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0) return (x, y);
        }
        catch { }
        return (96, 96);
    }
}

internal static class NativeMethods
{
    internal static readonly IntPtr HwndTop = IntPtr.Zero;
    internal static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }

    internal static System.Drawing.Rectangle GetWindowRectangle(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect)) return System.Drawing.Rectangle.Empty;
        return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    internal static string GetClassName(IntPtr handle)
    {
        var buffer = new System.Text.StringBuilder(128);
        _ = GetClassNameNative(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));

    internal static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr handle, out Rect rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(IntPtr handle, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr MonitorFromPoint(Point point, int flags);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr extraData);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    internal delegate bool EnumWindowsCallback(IntPtr handle, IntPtr extraData);
}
