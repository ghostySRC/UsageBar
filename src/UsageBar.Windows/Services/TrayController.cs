using System.Drawing;
using Forms = System.Windows.Forms;
using UsageBar.Core.Models;
using UsageBar.Core.Services;

namespace UsageBar.Windows.Services;

public sealed class TrayController : IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly Action<System.Drawing.Point> _openPopup;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _appIcon;
    private readonly Forms.ToolStripMenuItem _status;
    private bool _disposed;

    public TrayController(UsageCoordinator coordinator, Action<System.Drawing.Point> openPopup, Action openSettings, Action exit)
    {
        _coordinator = coordinator;
        _openPopup = openPopup;
        _openSettings = openSettings;
        _exit = exit;

        _status = new Forms.ToolStripMenuItem("Waiting for Codex or Antigravity") { Enabled = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open UsageBar", null, (_, _) => OpenPopupAtCursor());
        menu.Items.Add("Refresh now", null, (_, _) => _ = RefreshAsync());
        menu.Items.Add("Settings", null, (_, _) => _openSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Launch Codex", null, (_, _) => ApplicationLauncher.Launch("codex", _coordinator.Running));
        menu.Items.Add("Launch Antigravity", null, (_, _) => ApplicationLauncher.Launch("antigravity", _coordinator.Running));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit UsageBar", null, (_, _) => _exit());

        var executable = Forms.Application.ExecutablePath;
        _appIcon = System.Drawing.Icon.ExtractAssociatedIcon(executable) ?? (System.Drawing.Icon)SystemIcons.Application.Clone();
        _icon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "UsageBar · waiting for Codex or Antigravity",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += OnMouseClick;
        UpdateStatus();
    }

    public void UpdateStatus()
    {
        var visible = _coordinator.VisibleUsages;
        var lines = visible.SelectMany(usage => usage.Groups.Select(group =>
        {
            var shortName = usage.ProviderId == "codex" ? "CX" : "AG";
            var fiveHour = Format(usage, group.FiveHour);
            var weekly = Format(usage, group.Weekly);
            return $"{shortName} {fiveHour} · Week {weekly}";
        })).ToArray();
        var title = lines.Length == 0 ? "UsageBar · no monitored app open" : string.Join(" | ", lines);
        _status.Text = visible.Count == 0 ? "No monitored app open" : string.Join("  •  ", lines);
        _icon.Text = title.Length <= 63 ? title : title[..63];
    }

    private static string Format(ProviderUsage usage, UsageWindow? window)
    {
        if (window is null) return "—";
        var value = window.UsedPercent;
        return value is null ? "—" : $"{value:0}%";
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left) OpenPopupAtCursor();
    }

    private void OpenPopupAtCursor() => _openPopup(Forms.Cursor.Position);

    private async Task RefreshAsync()
    {
        try { await _coordinator.RefreshAsync(bypassBackoff: true); }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.MouseClick -= OnMouseClick;
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.Dispose();
        _appIcon.Dispose();
        menu?.Dispose();
    }
}
