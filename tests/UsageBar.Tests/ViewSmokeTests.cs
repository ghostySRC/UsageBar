using System.Windows;
using UsageBar.Core.Services;
using UsageBar.Windows;
using UsageBar.Windows.Models;
using UsageBar.Windows.Views;
using Xunit;

namespace UsageBar.Tests;

public sealed class ViewSmokeTests
{
    [Fact]
    public void PopupAndSettingsXamlInitializeWithoutShowingWindows()
    {
        Exception? failure = null;
        var directory = Path.Combine(Path.GetTempPath(), "UsageBar.Tests", Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                var coordinator = new UsageCoordinator(
                    settingsStore: new SettingsStore(directory),
                    cacheStore: new UsageCacheStore(directory));
                var settings = new SettingsWindow(coordinator, () => { });
                var popup = new UsagePopupWindow(coordinator, _ => { }, _ => { }, () => { });
                popup.RefreshData();

                Assert.NotNull(settings.FindName("StartWithWindowsBox"));
                Assert.NotNull(settings.FindName("RefreshIntervalBox"));
                Assert.NotNull(popup.FindName("EmptyText"));
                Assert.Empty(Assert.IsType<UsagePopupViewModel>(popup.DataContext).Providers);

                application.Shutdown();
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw new Xunit.Sdk.XunitException($"WPF view smoke test failed: {failure.GetType().Name}");

        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UsageBar.Tests")) + Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory);
        if (fullDirectory.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullDirectory))
        {
            Directory.Delete(fullDirectory, recursive: true);
        }
    }
}
