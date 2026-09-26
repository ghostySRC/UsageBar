using UsageBar.Core.Models;
using UsageBar.Core.Services;
using Xunit;

namespace UsageBar.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SettingsRoundTripAndInvalidValuesNormalize()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "UsageBar.Tests");
        var directory = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var settings = UsageBarSettings.Default with
            {
                RefreshIntervalSeconds = 45,
                WidgetLeft = double.NaN,
                WidgetTop = double.PositiveInfinity,
                WidgetMonitor = new string('M', 140)
            };

            store.Save(settings);
            var restored = store.Load();

            Assert.Equal(60, restored.RefreshIntervalSeconds);
            Assert.Null(restored.WidgetLeft);
            Assert.Null(restored.WidgetTop);
            Assert.Equal(128, restored.WidgetMonitor!.Length);
            Assert.True(restored.StartWithWindows);
            Assert.True(restored.ShowCodex);
            Assert.True(restored.ShowAntigravity);
        }
        finally
        {
            var expectedParent = Path.GetFullPath(basePath) + Path.DirectorySeparatorChar;
            var fullDirectory = Path.GetFullPath(directory);
            if (fullDirectory.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullDirectory))
            {
                Directory.Delete(fullDirectory, recursive: true);
            }
        }
    }
}
