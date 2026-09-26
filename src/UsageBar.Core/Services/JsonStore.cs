using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBar.Core.Models;

namespace UsageBar.Core.Services;

public sealed class SettingsStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public SettingsStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageBar");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "settings.json");
    }

    public UsageBarSettings Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(_filePath);
            if (bytes.Length is 0 or > 64 * 1024) throw new InvalidDataException();
            var settings = JsonSerializer.Deserialize<UsageBarSettings>(bytes, Options);
            if (settings is null || settings.Version > 1) throw new InvalidDataException();
            var normalized = settings.Normalize();
            if (normalized != settings) Save(normalized);
            return normalized;
        }
        catch (FileNotFoundException)
        {
            return UsageBarSettings.Default;
        }
        catch
        {
            AppLog.Warning("settings", "settings-reset");
            return UsageBarSettings.Default;
        }
    }

    public void Save(UsageBarSettings settings) => WriteAtomically(_filePath, JsonSerializer.SerializeToUtf8Bytes(settings.Normalize(), Options));

    internal static void WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    private static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class UsageCacheStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public UsageCacheStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageBar");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "usage-cache.json");
    }

    public IReadOnlyDictionary<string, ProviderUsage> Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(_filePath);
            if (bytes.Length is 0 or > 256 * 1024) return new Dictionary<string, ProviderUsage>();
            return JsonSerializer.Deserialize<Dictionary<string, ProviderUsage>>(bytes, Options)
                ?? new Dictionary<string, ProviderUsage>();
        }
        catch
        {
            return new Dictionary<string, ProviderUsage>();
        }
    }

    public void Save(IReadOnlyDictionary<string, ProviderUsage> snapshots)
    {
        var localOnly = snapshots
            .Where(pair => pair.Value.LastUpdated is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value with { Status = ProviderStatus.Stale, StatusDetail = null });
        SettingsStore.WriteAtomically(_filePath, JsonSerializer.SerializeToUtf8Bytes(localOnly, Options));
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageBar", "logs");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "usagebar.log");
    private const long MaxBytes = 1024 * 1024;

    public static void Information(string component, string eventName) => Write("INFO", component, eventName);

    public static void Warning(string component, string eventName) => Write("WARN", component, eventName);

    public static void Failure(string component, Exception exception) =>
        Write("WARN", component, "operation-failed-" + exception.GetType().Name);

    private static void Write(string level, string component, string eventName)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    var previous = FilePath + ".old";
                    try { File.Delete(previous); } catch { }
                    try { File.Move(FilePath, previous); } catch { }
                }
                var safeComponent = Sanitize(component);
                var safeEvent = Sanitize(eventName);
                var line = $"{DateTimeOffset.UtcNow:O} {level} {safeComponent} {safeEvent}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }

    private static string Sanitize(string value) => new(value.Where(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.').Take(64).ToArray());
}
