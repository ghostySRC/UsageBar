using System.Diagnostics;
using System.Runtime.InteropServices;
using UsageBar.Core.Models;
using UsageBar.Core.Services;

namespace UsageBar.Windows.Services;

public static class ApplicationLauncher
{
    private const string CodexAppUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private static readonly Guid ActivationManagerClassId = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    public static void Launch(string providerId, RunningApplications running)
    {
        if (providerId == "codex" && TryLaunchCodexDesktop()) return;

        var path = providerId switch
        {
            "codex" => running.CodexProcesses.FirstOrDefault()?.ExecutablePath ?? FindCodex(),
            "antigravity" => FindAntigravity(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AppLog.Warning("launcher", "app-not-found-" + providerId);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Failure("launcher", exception);
        }
    }

    private static bool TryLaunchCodexDesktop()
    {
        IApplicationActivationManager? activation = null;
        try
        {
            var activationType = Type.GetTypeFromCLSID(ActivationManagerClassId, throwOnError: true);
            activation = activationType is null ? null : (IApplicationActivationManager?)Activator.CreateInstance(activationType);
            if (activation is null) return false;
            var result = activation.ActivateApplication(CodexAppUserModelId, string.Empty, 0, out _);
            Marshal.ThrowExceptionForHR(result);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Failure("launcher", exception);
            return false;
        }
        finally
        {
            if (activation is not null && Marshal.IsComObject(activation)) Marshal.FinalReleaseComObject(activation);
        }
    }

    private static string? FindCodex()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try
        {
            return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? FindAntigravity()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        var candidates = new[]
        {
            Path.Combine(root, "Antigravity", "Antigravity.exe"),
            Path.Combine(root, "Antigravity IDE", "Antigravity IDE.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            uint options,
            out uint processId);
    }
}
