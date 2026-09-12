namespace CloudflareBox.Core;

public sealed record TransferMetadata(
    string OriginalName,
    string? OriginalLocation,
    string? MimeType,
    long? CreatedAtUnixMs,
    long? ModifiedAtUnixMs,
    string PlaintextSha256,
    int FormatVersion = 1);

public sealed record PendingTransfer(
    string Id,
    string State,
    long SizeBytes,
    long EncryptedSizeBytes,
    long PartSizeBytes,
    int PartCount,
    string WrappedKeyB64,
    string MetadataNonceB64,
    string MetadataCipherB64,
    int Priority,
    long CreatedAt);

public sealed record PendingTransfersResponse(IReadOnlyList<PendingTransfer> Transfers);
public sealed record DownloadUrlResponse(string TransferId, string Url, int ExpiresIn);
public sealed record PairingStartResponse(string PairingId, string WindowsDeviceId, string Code, long ExpiresAt, object QrPayload);
public sealed record PairingStatusResponse(string PairingId, string Status, string? WindowsDeviceId, string? AndroidDeviceId);
public sealed record ApiError(string Error, string Message);
public sealed record TransferResult(string TransferId, string FinalPath, string Sha256, bool DuplicateSkipped);

public sealed class ClientSettings
{
    public string ApiBase { get; set; } = "";
    public string? DeviceId { get; set; }
    public string DestinationDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
    public string WorkDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudflareBOX", "work");
    public int PollSeconds { get; set; } = 5;
    public long ReserveFreeBytes { get; set; } = 20L * 1024 * 1024 * 1024;
}
