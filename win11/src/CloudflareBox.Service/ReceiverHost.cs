using System.Security.Cryptography;
using CloudflareBox.Core;

namespace CloudflareBox.Service;

internal static class ReceiverHost
{
    public static async Task InitializeAsync(string apiBase, string setupToken, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Root);
        Directory.CreateDirectory(destination);
        using var signing = RSA.Create(3072);
        using var encryption = RSA.Create(3072);
        WindowsKeyStore.Save(AppPaths.SigningKey, signing.ExportPkcs8PrivateKey());
        WindowsKeyStore.Save(AppPaths.EncryptionKey, encryption.ExportPkcs8PrivateKey());

        using var api = new CloudflareBoxApiClient(apiBase);
        var start = await api.StartPairingAsync(
            setupToken,
            Environment.MachineName,
            Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(encryption.ExportSubjectPublicKeyInfo()),
            ct);

        var settings = new StoredSettings
        {
            ApiBase = apiBase.TrimEnd('/') + "/",
            WindowsDeviceId = start.WindowsDeviceId,
            DestinationDirectory = destination,
            PollSeconds = 5,
        };
        SettingsStore.Save(AppPaths.Settings, settings);
        SettingsStore.Save(AppPaths.PendingPairing, new PendingPairing(start.PairingId, start.WindowsDeviceId, start.Code, start.ExpiresAt, start.QrPayload));
        SettingsStore.Save(AppPaths.State, new { status = "pairing", code = start.Code, expiresAt = start.ExpiresAt });
        Console.WriteLine($"Pairing code: {start.Code}");
        Console.WriteLine($"Pairing payload: {AppPaths.PendingPairing}");
    }

    public static async Task RunAsync(bool once, CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        var pendingPair = File.Exists(AppPaths.PendingPairing) ? SettingsStore.Load<PendingPairing>(AppPaths.PendingPairing) : null;
        if (pendingPair is not null)
        {
            using var pairingApi = new CloudflareBoxApiClient(settings.ApiBase);
            var status = await pairingApi.PairingStatusAsync(pendingPair.PairingId, pendingPair.Code, ct);
            if (!string.Equals(status.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
            {
                SettingsStore.Save(AppPaths.State, new { status = status.Status, code = pendingPair.Code, expiresAt = pendingPair.ExpiresAt });
                if (once) return;
                throw new InvalidOperationException("Pairing is not confirmed yet. Complete pairing from the Android app first.");
            }
            File.Delete(AppPaths.PendingPairing);
        }

        using var signing = RSA.Create();
        signing.ImportPkcs8PrivateKey(WindowsKeyStore.Load(AppPaths.SigningKey), out _);
        using var encryption = RSA.Create();
        encryption.ImportPkcs8PrivateKey(WindowsKeyStore.Load(AppPaths.EncryptionKey), out _);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        api.ConfigureDevice(signing, settings.WindowsDeviceId);
        using var downloadHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var clientSettings = new ClientSettings
        {
            ApiBase = settings.ApiBase,
            DeviceId = settings.WindowsDeviceId,
            DestinationDirectory = settings.DestinationDirectory,
            WorkDirectory = AppPaths.Work,
            PollSeconds = settings.PollSeconds,
        };
        var engine = new ReceiverEngine(api, downloadHttp, encryption, clientSettings);
        using var history = new HistoryDb(AppPaths.HistoryDb);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(AppPaths.PauseFlag))
                {
                    SettingsStore.Save(AppPaths.State, new { status = "paused", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                    if (once) break;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, settings.PollSeconds)), ct);
                    continue;
                }
                var results = await engine.ReceivePendingAsync(ct);
                foreach (var result in results)
                {
                    var length = File.Exists(result.FinalPath) ? new FileInfo(result.FinalPath).Length : 0;
                    history.Record(result.TransferId, result.FinalPath, length, result.Sha256, result.DuplicateSkipped);
                }
                SettingsStore.Save(AppPaths.State, new { status = "online", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), completed = results.Count });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SettingsStore.Save(AppPaths.State, new { status = "error", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), message = ex.Message });
                if (once) throw;
            }
            if (once) break;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, settings.PollSeconds)), ct);
        }
    }
}



