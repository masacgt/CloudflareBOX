using System.Security.Cryptography;

namespace CloudflareBox.Core;

public sealed class ReceiverEngine(CloudflareBoxApiClient api, RSA encryptionPrivateKey, ClientSettings settings)
{
    public async Task<IReadOnlyList<TransferResult>> ReceivePendingAsync(CancellationToken ct = default)
    {
        var pending = await api.GetPendingAsync(ct);
        var results = new List<TransferResult>();
        foreach (var transfer in pending.Transfers)
            results.Add(await ReceiveOneAsync(transfer, ct));
        return results;
    }

    public async Task<TransferResult> ReceiveOneAsync(PendingTransfer transfer, CancellationToken ct = default)
    {
        EnsureDiskSpace(transfer.EncryptedSizeBytes + settings.ReserveFreeBytes);
        Directory.CreateDirectory(settings.WorkDirectory);
        Directory.CreateDirectory(settings.DestinationDirectory);
        var encryptedPath = Path.Combine(settings.WorkDirectory, transfer.Id + ".cbx.part");
        var plainPath = Path.Combine(settings.WorkDirectory, transfer.Id + ".plain.part");
        await api.DownloadEncryptedAsync(transfer.Id, encryptedPath, transfer.EncryptedSizeBytes, ct);
        var fileKey = TransferCrypto.UnwrapFileKey(transfer.WrappedKeyB64, encryptionPrivateKey);
        try
        {
            var metadata = TransferCrypto.DecryptMetadata(transfer.MetadataNonceB64, transfer.MetadataCipherB64, fileKey);
            await using var encrypted = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var plain = new FileStream(plainPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualHash = await ObjectDecryptor.RunAsync(encrypted, plain, transfer.SizeBytes, transfer.PartSizeBytes, transfer.PartCount, fileKey, ct);
            await plain.FlushAsync(ct);
            if (!actualHash.Equals(metadata.PlaintextSha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Plaintext SHA-256 mismatch");
            plain.Close();
            var committed = await CommitAsync(plainPath, metadata, actualHash, ct);
            await api.ConfirmPcSaveAsync(transfer.Id, actualHash, ct);
            TryDelete(encryptedPath);
            return new TransferResult(transfer.Id, committed.Path, actualHash, committed.Duplicate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    private async Task<(string Path, bool Duplicate)> CommitAsync(string plainPath, TransferMetadata metadata, string sha256, CancellationToken ct)
    {
        var safe = FilenamePolicy.Sanitize(metadata.OriginalName);
        var requested = Path.Combine(settings.DestinationDirectory, safe);
        if (File.Exists(requested))
        {
            var existingHash = await HashFileAsync(requested, ct);
            if (existingHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(plainPath);
                return (requested, true);
            }
        }
        var finalPath = FilenamePolicy.FindAvailablePath(settings.DestinationDirectory, safe);
        File.Move(plainPath, finalPath);
        if (metadata.ModifiedAtUnixMs is long modified)
            File.SetLastWriteTimeUtc(finalPath, DateTimeOffset.FromUnixTimeMilliseconds(modified).UtcDateTime);
        return (finalPath, false);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(input, ct);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private void EnsureDiskSpace(long required)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(settings.DestinationDirectory)) ?? throw new IOException("Destination drive cannot be determined");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.AvailableFreeSpace < required) throw new IOException("Destination drive does not have the required free space");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
