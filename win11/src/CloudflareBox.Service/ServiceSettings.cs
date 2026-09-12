using System.Text.Json;

namespace CloudflareBox.Service;

internal sealed class StoredSettings
{
    public string ApiBase { get; set; } = "";
    public string WindowsDeviceId { get; set; } = "";
    public string DestinationDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
    public int PollSeconds { get; set; } = 5;
}

internal sealed record PendingPairing(string PairingId, string WindowsDeviceId, string Code, long ExpiresAt, object QrPayload);

internal static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("CLOUDFLAREBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloudflareBOX");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string SigningKey => Path.Combine(Root, "signing.key.dpapi");
    public static string EncryptionKey => Path.Combine(Root, "encryption.key.dpapi");
    public static string PendingPairing => Path.Combine(Root, "pending-pairing.json");
    public static string HistoryDb => Path.Combine(Root, "history.db");
    public static string Work => Path.Combine(Root, "work");
    public static string State => Path.Combine(Root, "state.json");
    public static string PauseFlag => Path.Combine(Root, "paused.flag");
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Load<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException($"Invalid settings file: {path}");

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, Options));
    }
}


