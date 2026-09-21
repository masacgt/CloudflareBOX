using System.Text.Json;

namespace CloudflareBox.Service;

internal sealed class StoredSettings
{
    public string ApiBase { get; set; } = "";
    public string WindowsDeviceId { get; set; } = "";
    public string DestinationDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
    public int PollSeconds { get; set; } = 600;
    public string InstallationId { get; set; } = "";
    public string CloudflareAccountId { get; set; } = "";
    public string WorkerName { get; set; } = "";
    public string D1DatabaseId { get; set; } = "";
    public string D1DatabaseName { get; set; } = "";
    public string R2BucketName { get; set; } = "";
    public string OAuthClientId { get; set; } = "";
    public string WorkerBundlePath { get; set; } = "";
    public string SetupToken { get; set; } = "";
}

internal sealed record PendingPairing(string PairingId, string WindowsDeviceId, string Code, long ExpiresAt, object QrPayload);
internal sealed record CloudflareToken(string AccessToken, string? RefreshToken, long ExpiresAtUnix, string Scope, string ClientId);

internal static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("CLOUDFLAREBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloudflareBOX");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string SigningKey => Path.Combine(Root, "signing.key.dpapi");
    public static string EncryptionKey => Path.Combine(Root, "encryption.key.dpapi");
    public static string OAuthToken => Path.Combine(Root, "cloudflare.oauth.dpapi");
    public static string PendingPairing => Path.Combine(Root, "pending-pairing.json");
    public static string HistoryDb => Path.Combine(Root, "history.db");
    public static string Work => Path.Combine(Root, "work");
    public static string State => Path.Combine(Root, "state.json");
    public static string PauseFlag => Path.Combine(Root, "paused.flag");
    public static string DefaultRecoveryBundle => Path.Combine(Root, "cloudflarebox-recovery.cbxr");
}

internal static class SettingsStore
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Load<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException($"Invalid settings file: {path}");

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".new";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, Options));
        File.Move(temp, path, true);
    }
}
