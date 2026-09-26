using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UsageBar.Core.Models;
using UsageBar.Core.Services;
using UsageBar.Windows.Services;

namespace UsageBar.Windows.Views;

public partial class SettingsWindow : Window
{
    private readonly UsageCoordinator _coordinator;
    private readonly Action _resetPosition;
    private bool _isSaving;

    public SettingsWindow(UsageCoordinator coordinator, Action resetPosition)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _resetPosition = resetPosition;
        LoadSettings(coordinator.Settings);
        ThemeManager.Apply();
        Closing += OnClosing;
    }

    private void LoadSettings(UsageBarSettings settings)
    {
        ShowCodexBox.IsChecked = settings.ShowCodex;
        ShowAntigravityBox.IsChecked = settings.ShowAntigravity;
        ShowPercentagesBox.IsChecked = settings.ShowPercentages;
        ShowBarsBox.IsChecked = settings.ShowProgressBars;
        CompactModeBox.IsChecked = settings.CompactMode;
        WidgetEnabledBox.IsChecked = settings.TaskbarEdgeWidget;
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTop;
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
        SelectByTag(RefreshIntervalBox, settings.RefreshIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SelectByTag(PercentageModeBox, settings.ShowRemaining ? "remaining" : "used");
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveSettings()
    {
        if (_isSaving) return;
        _isSaving = true;
        try
        {
            var interval = (RefreshIntervalBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var mode = (PercentageModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var settings = (_coordinator.Settings with
            {
                ShowCodex = ShowCodexBox.IsChecked == true,
                ShowAntigravity = ShowAntigravityBox.IsChecked == true,
                RefreshIntervalSeconds = int.TryParse(interval, out var seconds) ? seconds : 60,
                ShowRemaining = string.Equals(mode, "remaining", StringComparison.OrdinalIgnoreCase),
                ShowPercentages = ShowPercentagesBox.IsChecked == true,
                ShowProgressBars = ShowBarsBox.IsChecked == true,
                CompactMode = CompactModeBox.IsChecked == true,
                TaskbarEdgeWidget = WidgetEnabledBox.IsChecked == true,
                AlwaysOnTop = AlwaysOnTopBox.IsChecked == true,
                StartWithWindows = StartWithWindowsBox.IsChecked == true
            }).Normalize();
            _coordinator.SaveSettings(settings);
            StartupManager.Apply(settings.StartWithWindows);
        }
        finally { _isSaving = false; }
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs args)
    {
        SaveSettings();
        _resetPosition();
    }

    private void Close_Click(object sender, RoutedEventArgs args)
    {
        SaveSettings();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs args) => SaveSettings();
}
