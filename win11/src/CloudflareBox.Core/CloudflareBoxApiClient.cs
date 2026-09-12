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

    public async Task<PairingStatusResponse> PairingStatusAsync(string pairingId, string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"pairing/status?id={Uri.EscapeDataString(pairingId)}&code={Uri.EscapeDataString(code)}");
        return await SendAsync<PairingStatusResponse>(request, ct);
    }

    public Task<PendingTransfersResponse> GetPendingAsync(CancellationToken ct = default) =>
        SendSignedAsync<PendingTransfersResponse>(HttpMethod.Get, "transfers/pending", null, ct);

    public Task<DownloadUrlResponse> GetDownloadUrlAsync(string transferId, CancellationToken ct = default) =>
        SendSignedAsync<DownloadUrlResponse>(HttpMethod.Post, $"transfers/{Uri.EscapeDataString(transferId)}/download-url", new { }, ct);

    public async Task ConfirmPcSaveAsync(string transferId, string sha256, CancellationToken ct = default)
    {
        await SendSignedAsync<JsonElement>(HttpMethod.Post, $"transfers/{Uri.EscapeDataString(transferId)}/pc-complete", new { sha256 }, ct);
    }

    public Task<JsonElement> GetStatusAsync(CancellationToken ct = default) =>
        SendSignedAsync<JsonElement>(HttpMethod.Get, "status", null, ct);

    private async Task<T> SendSignedAsync<T>(HttpMethod method, string relative, object? payload, CancellationToken ct)
    {
        if (signer is null) throw new InvalidOperationException("Device signer is not configured");
        var body = payload is null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(payload, json);
        using var request = new HttpRequestMessage(method, new Uri(http.BaseAddress!, relative));
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
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
