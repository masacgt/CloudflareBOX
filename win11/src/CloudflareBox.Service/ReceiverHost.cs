using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CloudflareBox.Core;

namespace CloudflareBox.Service;

internal static class ReceiverHost
{
    public static async Task<IReadOnlyList<(string Id, string Name)>> ConnectCloudflareAsync(string clientId, string? accountId, string workerBundlePath, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Root);
        Directory.CreateDirectory(destination);
        var fullBundlePath = Path.GetFullPath(workerBundlePath);
        if (!File.Exists(fullBundlePath)) throw new FileNotFoundException("The packaged CloudflareBOX Worker bundle is missing.", fullBundlePath);

        var token = await CloudflareOAuth.ConnectAsync(clientId, ct);
        var settings = File.Exists(AppPaths.Settings) ? SettingsStore.Load<StoredSettings>(AppPaths.Settings) : new StoredSettings();
        settings.OAuthClientId = clientId;
        settings.DestinationDirectory = destination;
        settings.WorkerBundlePath = fullBundlePath;
        EnsureKeys(settings);
        SettingsStore.Save(AppPaths.Settings, settings);

        using var provisioner = new CloudflareProvisioner(token.AccessToken);
        if (string.IsNullOrWhiteSpace(accountId) && !string.IsNullOrWhiteSpace(settings.CloudflareAccountId))
            accountId = settings.CloudflareAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            var accounts = await provisioner.ListAccountsAsync(ct);
            if (accounts.Count == 0) throw new InvalidOperationException("No Cloudflare account is available to this authorization.");
            if (accounts.Count > 1)
            {
                SettingsStore.Save(AppPaths.State, new { status = "account_selection_required", count = accounts.Count });
                return accounts;
            }
            accountId = accounts[0].Id;
        }

        await ProvisionAndPairAsync(settings, provisioner, accountId, fullBundlePath, ct);
        return Array.Empty<(string Id, string Name)>();
    }

    public static async Task CompleteCloudflareConnectionAsync(string accountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Cloudflare account ID is required.", nameof(accountId));
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        EnsureExistingKeys();
        if (string.IsNullOrWhiteSpace(settings.WorkerBundlePath) || !File.Exists(settings.WorkerBundlePath))
            throw new FileNotFoundException("The packaged CloudflareBOX Worker bundle is missing.", settings.WorkerBundlePath);
        var token = await CloudflareOAuth.GetAccessTokenAsync(ct);
        using var provisioner = new CloudflareProvisioner(token);
        var allowedAccounts = await provisioner.ListAccountsAsync(ct);
        if (!allowedAccounts.Any(a => string.Equals(a.Id, accountId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected Cloudflare account is not available to this authorization.");
        await ProvisionAndPairAsync(settings, provisioner, accountId, settings.WorkerBundlePath, ct);
    }

    public static async Task RepairCloudflareAsync(string? workerBundlePath, CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        EnsureExistingKeys();
        var token = await CloudflareOAuth.GetAccessTokenAsync(ct);
        using var provisioner = new CloudflareProvisioner(token);
        await provisioner.RepairAsync(settings, settings.CloudflareAccountId, workerBundlePath ?? settings.WorkerBundlePath, ct);
        await EnsureWindowsPairingAsync(settings, ct);
        SettingsStore.Save(AppPaths.Settings, settings);
    }

    public static async Task UnlinkCloudflareAsync(CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        var token = await CloudflareOAuth.GetAccessTokenAsync(ct);
        using var provisioner = new CloudflareProvisioner(token);
        await provisioner.UnlinkAsync(settings, ct);
        await CloudflareOAuth.RevokeAsync(ct);
        settings.ApiBase = "";
        settings.WindowsDeviceId = "";
        settings.CloudflareAccountId = "";
        settings.D1DatabaseId = "";
        SettingsStore.Save(AppPaths.Settings, settings);
        if (File.Exists(AppPaths.PendingPairing)) File.Delete(AppPaths.PendingPairing);
        SettingsStore.Save(AppPaths.State, new { status = "unlinked" });
    }

    public static async Task InitializeAsync(string apiBase, string setupToken, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Root);
        Directory.CreateDirectory(destination);
        var settings = File.Exists(AppPaths.Settings) ? SettingsStore.Load<StoredSettings>(AppPaths.Settings) : new StoredSettings();
        settings.ApiBase = apiBase.TrimEnd('/') + "/";
        settings.SetupToken = setupToken;
        settings.DestinationDirectory = destination;
        EnsureKeys(settings);
        await StartInitialPairingAsync(settings, ct);
        SettingsStore.Save(AppPaths.Settings, settings);
    }

    public static async Task<DiagnosticsResponse> DiagnoseAsync(CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        using var signing = LoadRsa(AppPaths.SigningKey);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        api.ConfigureDevice(signing, settings.WindowsDeviceId);
        return await api.DiagnosticsAsync(ct);
    }

    public static async Task<PairingStartResponse> CreateAdditionalPairingAsync(CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        using var signing = LoadRsa(AppPaths.SigningKey);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        api.ConfigureDevice(signing, settings.WindowsDeviceId);
        return await api.StartAdditionalPairingAsync(ct);
    }

    public static async Task<PairingStatusResponse> ApprovePairingAsync(string pairingId, string code, CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        return await api.ApprovePairingAsync(settings.SetupToken, pairingId, code, ct);
    }

    public static async Task<DeviceListResponse> ListDevicesAsync(CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        using var signing = LoadRsa(AppPaths.SigningKey);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        api.ConfigureDevice(signing, settings.WindowsDeviceId);
        return await api.GetDevicesAsync(ct);
    }

    public static async Task RevokeDeviceAsync(string deviceId, CancellationToken ct)
    {
        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        using var signing = LoadRsa(AppPaths.SigningKey);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        api.ConfigureDevice(signing, settings.WindowsDeviceId);
        await api.RevokeDeviceAsync(deviceId, ct);
    }

    public static string ExportRecovery(string path, string? recoveryCode)
    {
        var code = string.IsNullOrWhiteSpace(recoveryCode) ? RecoveryManager.GenerateRecoveryCode() : recoveryCode;
        RecoveryManager.Export(path, code);
        return code;
    }

    public static void ImportRecovery(string path, string recoveryCode) => RecoveryManager.Import(path, recoveryCode);

    public static async Task RunAsync(bool once, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pollSeconds = 5;
            try
            {
                if (!File.Exists(AppPaths.Settings))
                {
                    SettingsStore.Save(AppPaths.State, new { status = "not_connected", message = "Cloudflare connection setup is required." });
                    if (once) return;
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                    continue;
                }

                var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
                pollSeconds = Math.Max(2, settings.PollSeconds);
                if (string.IsNullOrWhiteSpace(settings.ApiBase) || string.IsNullOrWhiteSpace(settings.WindowsDeviceId))
                {
                    var waitingStatus = File.Exists(AppPaths.OAuthToken) && string.IsNullOrWhiteSpace(settings.CloudflareAccountId)
                        ? "account_selection_required"
                        : "not_connected";
                    SettingsStore.Save(AppPaths.State, new { status = waitingStatus, message = "Cloudflare connection setup is required." });
                    if (once) return;
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                    continue;
                }

                if (File.Exists(AppPaths.PendingPairing))
                {
                    var pendingPair = SettingsStore.Load<PendingPairing>(AppPaths.PendingPairing);
                    using var pairingApi = new CloudflareBoxApiClient(settings.ApiBase);
                    var pairingStatus = await pairingApi.PairingStatusAsync(pendingPair.PairingId, pendingPair.Code, ct);
                    if (string.Equals(pairingStatus.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(AppPaths.PendingPairing);
                    }
                    else if (string.Equals(pairingStatus.Status, "expired", StringComparison.OrdinalIgnoreCase))
                    {
                        await StartInitialPairingAsync(settings, ct);
                        SettingsStore.Save(AppPaths.Settings, settings);
                        if (once) return;
                        await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                        continue;
                    }
                    else
                    {
                        SettingsStore.Save(AppPaths.State, new { status = pairingStatus.Status, code = pendingPair.Code, expiresAt = pendingPair.ExpiresAt });
                        if (once) return;
                        await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                        continue;
                    }
                }

                if (File.Exists(AppPaths.PauseFlag))
                {
                    SettingsStore.Save(AppPaths.State, new { status = "paused", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                    if (once) return;
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                    continue;
                }

                using var signing = LoadRsa(AppPaths.SigningKey);
                using var encryption = LoadRsa(AppPaths.EncryptionKey);
                using var api = new CloudflareBoxApiClient(settings.ApiBase);
                api.ConfigureDevice(signing, settings.WindowsDeviceId);
                var clientSettings = new ClientSettings
                {
                    ApiBase = settings.ApiBase,
                    DeviceId = settings.WindowsDeviceId,
                    DestinationDirectory = settings.DestinationDirectory,
                    WorkDirectory = AppPaths.Work,
                    PollSeconds = settings.PollSeconds,
                };
                var engine = new ReceiverEngine(api, encryption, clientSettings);
                using var history = new HistoryDb(AppPaths.HistoryDb);
                var results = await engine.ReceivePendingAsync(ct);
                foreach (var result in results)
                {
                    var length = File.Exists(result.FinalPath) ? new FileInfo(result.FinalPath).Length : 0;
                    history.Record(result.TransferId, result.FinalPath, length, result.Sha256, result.DuplicateSkipped);
                }
                SettingsStore.Save(AppPaths.State, new { status = "online", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), completed = results.Count });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SettingsStore.Save(AppPaths.State, new { status = "error", lastCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), message = ex.Message });
                if (once) throw;
            }

            if (once) break;
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
        }
    }

    private static async Task ProvisionAndPairAsync(StoredSettings settings, CloudflareProvisioner provisioner, string accountId, string workerBundlePath, CancellationToken ct)
    {
        await provisioner.RepairAsync(settings, accountId, workerBundlePath, ct);
        await EnsureWindowsPairingAsync(settings, ct);
        SettingsStore.Save(AppPaths.Settings, settings);
    }

    private static void EnsureKeys(StoredSettings settings)
    {
        var signingExists = File.Exists(AppPaths.SigningKey);
        var encryptionExists = File.Exists(AppPaths.EncryptionKey);
        if (signingExists != encryptionExists) throw new InvalidOperationException("One Windows key is missing. Restore the recovery bundle before continuing.");
        if (signingExists) return;
        if (!string.IsNullOrWhiteSpace(settings.WindowsDeviceId)) throw new InvalidOperationException("Windows keys are missing. Restore the recovery bundle before continuing.");
        using var signing = RSA.Create(3072);
        using var encryption = RSA.Create(3072);
        WindowsKeyStore.Save(AppPaths.SigningKey, signing.ExportPkcs8PrivateKey());
        WindowsKeyStore.Save(AppPaths.EncryptionKey, encryption.ExportPkcs8PrivateKey());
    }

    private static void EnsureExistingKeys()
    {
        if (!File.Exists(AppPaths.SigningKey) || !File.Exists(AppPaths.EncryptionKey))
            throw new InvalidOperationException("Windows keys are missing. Restore the recovery bundle before repair.");
    }

    private static async Task EnsureWindowsPairingAsync(StoredSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.WindowsDeviceId))
        {
            await StartInitialPairingAsync(settings, ct);
            return;
        }
        try
        {
            using var signing = LoadRsa(AppPaths.SigningKey);
            using var api = new CloudflareBoxApiClient(settings.ApiBase);
            api.ConfigureDevice(signing, settings.WindowsDeviceId);
            await api.DiagnosticsAsync(ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
        {
            settings.WindowsDeviceId = "";
            await StartInitialPairingAsync(settings, ct);
        }
    }

    private static async Task StartInitialPairingAsync(StoredSettings settings, CancellationToken ct)
    {
        using var signing = LoadRsa(AppPaths.SigningKey);
        using var encryption = LoadRsa(AppPaths.EncryptionKey);
        using var api = new CloudflareBoxApiClient(settings.ApiBase);
        var start = await api.StartPairingAsync(
            settings.SetupToken,
            Environment.MachineName,
            Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(encryption.ExportSubjectPublicKeyInfo()),
            ct);
        settings.WindowsDeviceId = start.WindowsDeviceId;
        SaveInitialPairing(start);
    }

    private static void SaveInitialPairing(PairingStartResponse start)
    {
        SettingsStore.Save(AppPaths.PendingPairing, new PendingPairing(start.PairingId, start.WindowsDeviceId, start.Code, start.ExpiresAt, start.QrPayload));
        SettingsStore.Save(AppPaths.State, new { status = "pairing", code = start.Code, expiresAt = start.ExpiresAt });
        Console.WriteLine($"Pairing code: {start.Code}");
        Console.WriteLine($"Pairing payload: {AppPaths.PendingPairing}");
    }

    private static RSA LoadRsa(string path)
    {
        var raw = WindowsKeyStore.Load(path);
        try
        {
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(raw, out _);
            return rsa;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }
}
