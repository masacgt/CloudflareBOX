using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudflareBox.Service;

internal sealed class CloudflareProvisioner : IDisposable
{
    private const string ApiRoot = "https://api.cloudflare.com/client/v4/";
    private readonly HttpClient http;

    public CloudflareProvisioner(string accessToken)
    {
        http = new HttpClient { BaseAddress = new Uri(ApiRoot), Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> ListAccountsAsync(CancellationToken ct)
    {
        using var doc = await GetResultAsync("accounts?per_page=50", ct);
        var list = new List<(string, string)>();
        foreach (var item in doc.RootElement.EnumerateArray())
            list.Add((item.GetProperty("id").GetString()!, item.GetProperty("name").GetString() ?? "Cloudflare account"));
        return list;
    }

    public async Task RepairAsync(StoredSettings settings, string? accountId, string workerBundlePath, CancellationToken ct)
    {
        InitializeNames(settings);
        if (string.IsNullOrWhiteSpace(settings.CloudflareAccountId))
        {
            if (!string.IsNullOrWhiteSpace(accountId)) settings.CloudflareAccountId = accountId;
            else
            {
                var accounts = await ListAccountsAsync(ct);
                if (accounts.Count != 1) throw new InvalidOperationException("Cloudflare account selection is required because more than one account is available.");
                settings.CloudflareAccountId = accounts[0].Id;
            }
        }
        if (!string.IsNullOrWhiteSpace(accountId) && !string.Equals(accountId, settings.CloudflareAccountId, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested Cloudflare account does not match this installation.");

        settings.WorkerBundlePath = Path.GetFullPath(workerBundlePath);
        if (!File.Exists(settings.WorkerBundlePath)) throw new FileNotFoundException("The packaged CloudflareBOX Worker bundle is missing.", settings.WorkerBundlePath);

        await EnsureWorkersSubdomainAsync(settings, ct);
        await EnsureR2Async(settings, ct);
        await EnsureD1Async(settings, ct);
        await ApplySchemaAsync(settings, ct);
        await DeployWorkerAsync(settings, ct);
        await EnableWorkerAsync(settings, ct);
        await ConfigureSchedulesAsync(settings, ct);
        settings.ApiBase = await ResolveApiBaseAsync(settings, ct);
        SettingsStore.Save(AppPaths.Settings, settings);
    }

    public async Task UnlinkAsync(StoredSettings settings, CancellationToken ct)
    {
        ValidateOwnedNames(settings);
        if (await HasPendingTransfersAsync(settings, ct)) throw new InvalidOperationException("Transfers are still pending. Finish or cancel them before unlinking Cloudflare.");

        var bucketPath = $"accounts/{settings.CloudflareAccountId}/r2/buckets/{Uri.EscapeDataString(settings.R2BucketName)}";
        using (var bucketDelete = await SendAsync(HttpMethod.Delete, bucketPath, null, ct, allowNotFound: true))
        {
            if (bucketDelete.StatusCode != HttpStatusCode.NotFound && !bucketDelete.IsSuccessStatusCode)
                throw await ErrorAsync("R2 bucket could not be removed. Wait for temporary objects to be cleaned and run unlink again.", bucketDelete, ct);
        }

        await DeleteAllowMissingAsync($"accounts/{settings.CloudflareAccountId}/workers/scripts/{Uri.EscapeDataString(settings.WorkerName)}", ct);
        if (!string.IsNullOrWhiteSpace(settings.D1DatabaseId))
            await DeleteAllowMissingAsync($"accounts/{settings.CloudflareAccountId}/d1/database/{Uri.EscapeDataString(settings.D1DatabaseId)}", ct);
    }

    private static void InitializeNames(StoredSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.InstallationId)) settings.InstallationId = Guid.NewGuid().ToString("N");
        var shortId = new string(settings.InstallationId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (shortId.Length < 12) throw new InvalidDataException("Invalid installation ID.");
        shortId = shortId[..12];
        var prefix = $"cloudflarebox-{shortId}";
        settings.WorkerName = string.IsNullOrWhiteSpace(settings.WorkerName) ? $"{prefix}-worker" : settings.WorkerName;
        settings.D1DatabaseName = string.IsNullOrWhiteSpace(settings.D1DatabaseName) ? $"{prefix}-db" : settings.D1DatabaseName;
        settings.R2BucketName = string.IsNullOrWhiteSpace(settings.R2BucketName) ? $"{prefix}-files" : settings.R2BucketName;
        settings.SetupToken = string.IsNullOrWhiteSpace(settings.SetupToken) ? Base64Url(RandomNumberGenerator.GetBytes(32)) : settings.SetupToken;
        ValidateOwnedNames(settings);
    }

    private static void ValidateOwnedNames(StoredSettings settings)
    {
        var id = new string((settings.InstallationId ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (id.Length < 12) throw new InvalidDataException("Installation ownership marker is missing.");
        var prefix = $"cloudflarebox-{id[..12]}";
        if (settings.WorkerName != $"{prefix}-worker" || settings.D1DatabaseName != $"{prefix}-db" || settings.R2BucketName != $"{prefix}-files")
            throw new InvalidOperationException("Cloudflare resource names do not match this installation. Cleanup was refused.");
    }

    private async Task EnsureWorkersSubdomainAsync(StoredSettings settings, CancellationToken ct)
    {
        var path = $"accounts/{settings.CloudflareAccountId}/workers/subdomain";
        using (var existing = await SendAsync(HttpMethod.Get, path, null, ct, allowNotFound: true))
        {
            if (existing.IsSuccessStatusCode)
            {
                using var result = await ReadResultAsync(existing, "workers.dev inspection failed.", ct);
                if (result.RootElement.TryGetProperty("subdomain", out var current) && !string.IsNullOrWhiteSpace(current.GetString())) return;
            }
            else if (existing.StatusCode != HttpStatusCode.NotFound)
            {
                throw await ErrorAsync("workers.dev inspection failed.", existing, ct);
            }
        }

        var marker = new string(settings.InstallationId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (marker.Length < 20) marker = marker.PadRight(20, '0');
        var subdomain = "cloudflarebox-" + marker[..20];
        using var created = await SendAsync(HttpMethod.Put, path, new { subdomain }, ct);
        await EnsureSuccessAsync(created, "workers.dev subdomain creation failed.", ct);
    }

    private async Task EnsureR2Async(StoredSettings settings, CancellationToken ct)
    {
        var path = $"accounts/{settings.CloudflareAccountId}/r2/buckets/{Uri.EscapeDataString(settings.R2BucketName)}";
        using var existing = await SendAsync(HttpMethod.Get, path, null, ct, allowNotFound: true);
        if (existing.IsSuccessStatusCode) return;
        if (existing.StatusCode != HttpStatusCode.NotFound) throw await ErrorAsync("R2 inspection failed.", existing, ct);
        using var created = await SendAsync(HttpMethod.Post, $"accounts/{settings.CloudflareAccountId}/r2/buckets", new { name = settings.R2BucketName, storageClass = "Standard" }, ct);
        await EnsureSuccessAsync(created, "R2 creation failed.", ct);
    }

    private async Task EnsureD1Async(StoredSettings settings, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.D1DatabaseId))
        {
            using var found = await SendAsync(HttpMethod.Get, $"accounts/{settings.CloudflareAccountId}/d1/database/{Uri.EscapeDataString(settings.D1DatabaseId)}", null, ct, allowNotFound: true);
            if (found.IsSuccessStatusCode) return;
            if (found.StatusCode != HttpStatusCode.NotFound) throw await ErrorAsync("D1 inspection failed.", found, ct);
            settings.D1DatabaseId = "";
        }

        using (var listed = await GetResultAsync($"accounts/{settings.CloudflareAccountId}/d1/database?name={Uri.EscapeDataString(settings.D1DatabaseName)}&per_page=10", ct))
        {
            foreach (var item in listed.RootElement.EnumerateArray())
            {
                if (string.Equals(item.GetProperty("name").GetString(), settings.D1DatabaseName, StringComparison.Ordinal))
                {
                    settings.D1DatabaseId = item.GetProperty("uuid").GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(settings.D1DatabaseId)) return;
                }
            }
        }

        using var created = await SendAsync(HttpMethod.Post, $"accounts/{settings.CloudflareAccountId}/d1/database", new { name = settings.D1DatabaseName }, ct);
        using var result = await ReadResultAsync(created, "D1 creation failed.", ct);
        settings.D1DatabaseId = result.RootElement.GetProperty("uuid").GetString() ?? throw new InvalidDataException("D1 database ID was not returned.");
    }

    private async Task ApplySchemaAsync(StoredSettings settings, CancellationToken ct)
    {
        foreach (var statement in SchemaStatements)
        {
            using var response = await SendAsync(HttpMethod.Post, $"accounts/{settings.CloudflareAccountId}/d1/database/{settings.D1DatabaseId}/query", new { sql = statement }, ct);
            await EnsureSuccessAsync(response, "D1 schema repair failed.", ct);
        }
    }

    private async Task DeployWorkerAsync(StoredSettings settings, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            main_module = "worker.js",
            compatibility_date = "2026-09-12",
            compatibility_flags = new[] { "nodejs_compat" },
            bindings = new object[]
            {
                new { type = "d1", name = "DB", database_id = settings.D1DatabaseId },
                new { type = "r2_bucket", name = "FILES", bucket_name = settings.R2BucketName },
                new { type = "plain_text", name = "APP_VERSION", text = "2" },
                new { type = "plain_text", name = "R2_BUCKET_NAME", text = settings.R2BucketName },
                new { type = "plain_text", name = "FREE_STORAGE_GB_MONTH", text = "10" },
                new { type = "plain_text", name = "FREE_CLASS_A", text = "1000000" },
                new { type = "plain_text", name = "FREE_CLASS_B", text = "10000000" },
                new { type = "plain_text", name = "FREE_STOP_RATIO", text = "0.90" },
                new { type = "plain_text", name = "ADMIN_EMAIL", text = "" },
                new { type = "secret_text", name = "PAIRING_SETUP_TOKEN", text = settings.SetupToken },
            },
        });
        using var multipart = new MultipartFormDataContent();
        var meta = new StringContent(metadata, Encoding.UTF8, "application/json");
        multipart.Add(meta, "metadata");
        var module = new ByteArrayContent(await File.ReadAllBytesAsync(settings.WorkerBundlePath, ct));
        module.Headers.ContentType = new MediaTypeHeaderValue("application/javascript+module");
        multipart.Add(module, "worker.js", "worker.js");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"accounts/{settings.CloudflareAccountId}/workers/scripts/{Uri.EscapeDataString(settings.WorkerName)}") { Content = multipart };
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, "Worker deployment failed.", ct);
    }

    private async Task EnableWorkerAsync(StoredSettings settings, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"accounts/{settings.CloudflareAccountId}/workers/scripts/{Uri.EscapeDataString(settings.WorkerName)}/subdomain", new { enabled = true, previews_enabled = false }, ct);
        await EnsureSuccessAsync(response, "workers.dev activation failed.", ct);
    }

    private async Task ConfigureSchedulesAsync(StoredSettings settings, CancellationToken ct)
    {
        var schedules = new[] { new { cron = "*/30 * * * *" }, new { cron = "17 3 * * *" } };
        using var response = await SendAsync(HttpMethod.Put, $"accounts/{settings.CloudflareAccountId}/workers/scripts/{Uri.EscapeDataString(settings.WorkerName)}/schedules", schedules, ct);
        await EnsureSuccessAsync(response, "Worker schedule configuration failed.", ct);
    }

    private async Task<string> ResolveApiBaseAsync(StoredSettings settings, CancellationToken ct)
    {
        using var result = await GetResultAsync($"accounts/{settings.CloudflareAccountId}/workers/subdomain", ct);
        var subdomain = result.RootElement.GetProperty("subdomain").GetString();
        if (string.IsNullOrWhiteSpace(subdomain)) throw new InvalidOperationException("workers.dev subdomain is unavailable after Cloudflare repair.");
        return $"https://{settings.WorkerName}.{subdomain}.workers.dev/api/v1/";
    }

    private async Task<bool> HasPendingTransfersAsync(StoredSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.D1DatabaseId)) return false;
        using var response = await SendAsync(HttpMethod.Post, $"accounts/{settings.CloudflareAccountId}/d1/database/{settings.D1DatabaseId}/query", new { sql = "SELECT COUNT(*) AS n FROM transfers WHERE state NOT IN ('COMPLETE','CANCELED')" }, ct, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        using var doc = await ReadResultAsync(response, "Transfer safety check failed.", ct);
        foreach (var queryResult in doc.RootElement.EnumerateArray())
        {
            if (queryResult.TryGetProperty("results", out var rows) && rows.GetArrayLength() > 0 && rows[0].TryGetProperty("n", out var count))
                return count.GetInt64() > 0;
        }
        return false;
    }

    private async Task<JsonDocument> GetResultAsync(string relative, CancellationToken ct)
    {
        using var response = await http.GetAsync(relative, ct);
        return await ReadResultAsync(response, "Cloudflare API request failed.", ct);
    }

    private async Task<JsonDocument> ReadResultAsync(HttpResponseMessage response, string message, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{message} HTTP {(int)response.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
        using var envelope = JsonDocument.Parse(bytes);
        if (!envelope.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean() || !envelope.RootElement.TryGetProperty("result", out var result))
            throw new InvalidDataException($"{message} Cloudflare returned an invalid result.");
        return JsonDocument.Parse(result.GetRawText());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? payload, CancellationToken ct, bool allowNotFound = false)
    {
        var request = new HttpRequestMessage(method, relative);
        if (payload is not null) request.Content = JsonContent.Create(payload, options: SettingsStore.Options);
        var response = await http.SendAsync(request, ct);
        if (!allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return response;
        return response;
    }

    private async Task DeleteAllowMissingAsync(string relative, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, relative, null, ct, allowNotFound: true);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, "Cloudflare cleanup failed.", ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string message, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        throw await ErrorAsync(message, response, ct);
    }

    private static async Task<Exception> ErrorAsync(string message, HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        return new InvalidOperationException($"{message} HTTP {(int)response.StatusCode}: {text}");
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static readonly string[] SchemaStatements =
    {
        "CREATE TABLE IF NOT EXISTS devices (id TEXT PRIMARY KEY,kind TEXT NOT NULL CHECK(kind IN ('android','windows')),name TEXT NOT NULL,signing_public_key_spki_b64 TEXT NOT NULL,encryption_public_key_spki_b64 TEXT,revoked_at INTEGER,last_seen_at INTEGER,created_at INTEGER NOT NULL)",
        "CREATE TABLE IF NOT EXISTS pairings (id TEXT PRIMARY KEY,code_hash TEXT NOT NULL,windows_device_id TEXT NOT NULL,windows_name TEXT NOT NULL,windows_signing_public_key_spki_b64 TEXT NOT NULL,windows_encryption_public_key_spki_b64 TEXT NOT NULL,android_device_id TEXT,status TEXT NOT NULL CHECK(status IN ('pending','confirmed','expired')),expires_at INTEGER NOT NULL,created_at INTEGER NOT NULL)",
        "CREATE TABLE IF NOT EXISTS request_nonces (device_id TEXT NOT NULL,nonce TEXT NOT NULL,expires_at INTEGER NOT NULL,PRIMARY KEY(device_id,nonce))",
        "CREATE TABLE IF NOT EXISTS transfers (id TEXT PRIMARY KEY,android_device_id TEXT NOT NULL,windows_device_id TEXT NOT NULL,object_key TEXT NOT NULL UNIQUE,size_bytes INTEGER NOT NULL,encrypted_size_bytes INTEGER NOT NULL,part_size_bytes INTEGER NOT NULL,part_count INTEGER NOT NULL,plaintext_sha256 TEXT,wrapped_key_b64 TEXT NOT NULL,metadata_nonce_b64 TEXT NOT NULL,metadata_cipher_b64 TEXT NOT NULL,multipart_upload_id TEXT,state TEXT NOT NULL,priority INTEGER NOT NULL DEFAULT 1,error_code TEXT,created_at INTEGER NOT NULL,updated_at INTEGER NOT NULL,r2_ready_at INTEGER,pc_saved_at INTEGER,delete_after INTEGER,completed_at INTEGER,canceled_at INTEGER)",
        "CREATE INDEX IF NOT EXISTS transfers_state_idx ON transfers(state,updated_at)",
        "CREATE INDEX IF NOT EXISTS transfers_android_idx ON transfers(android_device_id,created_at DESC)",
        "CREATE INDEX IF NOT EXISTS transfers_windows_idx ON transfers(windows_device_id,created_at DESC)",
        "CREATE INDEX IF NOT EXISTS transfers_windows_state_created_idx ON transfers(windows_device_id,state,created_at)",
        "CREATE TABLE IF NOT EXISTS transfer_parts (transfer_id TEXT NOT NULL,part_number INTEGER NOT NULL,etag TEXT NOT NULL,encrypted_sha256 TEXT NOT NULL,size_bytes INTEGER NOT NULL,completed_at INTEGER NOT NULL,PRIMARY KEY(transfer_id,part_number))",
        "CREATE TABLE IF NOT EXISTS pc_commands (id TEXT PRIMARY KEY,windows_device_id TEXT NOT NULL,kind TEXT NOT NULL,payload_json TEXT NOT NULL,state TEXT NOT NULL CHECK(state IN ('pending','acked','expired')),created_at INTEGER NOT NULL,expires_at INTEGER NOT NULL,acked_at INTEGER)",
        "CREATE TABLE IF NOT EXISTS usage_daily (day TEXT PRIMARY KEY,peak_stored_bytes INTEGER NOT NULL DEFAULT 0,class_a_ops INTEGER NOT NULL DEFAULT 0,class_b_ops INTEGER NOT NULL DEFAULT 0,updated_at INTEGER NOT NULL)",
    };

    public void Dispose() => http.Dispose();
}
