using Microsoft.Win32;

namespace UsageBar.Windows.Services;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UsageBar";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;
            var normalizedPath = executable.Replace('/', '\\');
            if (normalizedPath.Contains("\\src\\UsageBar.Windows\\bin\\", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
        catch (Exception exception)
        {
            UsageBar.Core.Services.AppLog.Failure("startup", exception);
        }
    }
}
