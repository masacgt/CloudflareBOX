using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace CloudflareBox.Core;

public sealed class CloudflareBoxApiClient : IDisposable
{
    private readonly HttpClient http;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private RequestSigner? signer;

    public CloudflareBoxApiClient(string apiBase)
    {
        http = new HttpClient { BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public void ConfigureDevice(RSA signingKey, string deviceId) => signer = new RequestSigner(signingKey, deviceId);

    public async Task<PairingStartResponse> StartPairingAsync(string setupToken, string name, string signingPublicKeySpkiB64, string encryptionPublicKeySpkiB64, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "pairing/start");
        request.Headers.Add("X-CB-Setup-Token", setupToken);
        request.Content = JsonContent.Create(new { name, signingPublicKeySpkiB64, encryptionPublicKeySpkiB64 }, options: json);
        return await SendAsync<PairingStartResponse>(request, ct);
    }

    public Task<PairingStartResponse> StartAdditionalPairingAsync(CancellationToken ct = default) =>
        SendSignedAsync<PairingStartResponse>(HttpMethod.Post, "devices/pairing", new { }, ct);

    public async Task<PairingStatusResponse> PairingStatusAsync(string pairingId, string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"pairing/status?id={Uri.EscapeDataString(pairingId)}&code={Uri.EscapeDataString(code)}");
        return await SendAsync<PairingStatusResponse>(request, ct);
    }

    public Task<PendingTransfersResponse> GetPendingAsync(CancellationToken ct = default) =>
        SendSignedAsync<PendingTransfersResponse>(HttpMethod.Get, "transfers/pending", null, ct);

    public Task<DiagnosticsResponse> DiagnosticsAsync(CancellationToken ct = default) =>
        SendSignedAsync<DiagnosticsResponse>(HttpMethod.Get, "diagnostics", null, ct);

    public Task<DeviceListResponse> GetDevicesAsync(CancellationToken ct = default) =>
        SendSignedAsync<DeviceListResponse>(HttpMethod.Get, "devices", null, ct);

    public Task<JsonElement> RevokeDeviceAsync(string deviceId, CancellationToken ct = default) =>
        SendSignedAsync<JsonElement>(HttpMethod.Post, $"devices/{Uri.EscapeDataString(deviceId)}/revoke", new { }, ct);

    public async Task ConfirmPcSaveAsync(string transferId, string sha256, CancellationToken ct = default)
    {
        await SendSignedAsync<JsonElement>(HttpMethod.Post, $"transfers/{Uri.EscapeDataString(transferId)}/pc-complete", new { sha256 }, ct);
    }

    public Task<JsonElement> GetStatusAsync(CancellationToken ct = default) =>
        SendSignedAsync<JsonElement>(HttpMethod.Get, "status", null, ct);

    public async Task DownloadEncryptedAsync(string transferId, string path, long expectedSize, CancellationToken ct = default)
    {
        if (signer is null) throw new InvalidOperationException("Device signer is not configured");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existing = File.Exists(path) ? new FileInfo(path).Length : 0L;
        if (existing == expectedSize) return;
        if (existing < 0 || existing > expectedSize)
        {
            File.Delete(path);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(http.BaseAddress!, $"transfers/{Uri.EscapeDataString(transferId)}/content"));
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        signer.Apply(request, Array.Empty<byte>());
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            ApiError? error = null;
            try { error = JsonSerializer.Deserialize<ApiError>(bytes, json); } catch { }
            throw new HttpRequestException(error?.Message ?? $"API error {(int)response.StatusCode}", null, response.StatusCode);
        }

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existing > 0 && !append) existing = 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, ct);
        await output.FlushAsync(ct);
        var finalSize = new FileInfo(path).Length;
        if (finalSize != expectedSize) throw new IOException($"Encrypted download length mismatch. expected={expectedSize} actual={finalSize}");
    }

    private async Task<T> SendSignedAsync<T>(HttpMethod method, string relative, object? payload, CancellationToken ct)
    {
        if (signer is null) throw new InvalidOperationException("Device signer is not configured");
        var body = payload is null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(payload, json);
        using var request = new HttpRequestMessage(method, new Uri(http.BaseAddress!, relative));
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        signer.Apply(request, body);
        return await SendAsync<T>(request, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = JsonSerializer.Deserialize<ApiError>(bytes, json); } catch { }
            throw new HttpRequestException(error?.Message ?? $"API error {(int)response.StatusCode}", null, response.StatusCode);
        }
        return JsonSerializer.Deserialize<T>(bytes, json) ?? throw new InvalidDataException("API returned invalid JSON");
    }

    public void Dispose() => http.Dispose();
}
