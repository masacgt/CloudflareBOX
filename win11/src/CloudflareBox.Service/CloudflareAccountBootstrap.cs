using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CloudflareBox.Service;

internal sealed class CloudflareAccountBootstrap : IDisposable
{
    private readonly HttpClient http;

    public CloudflareAccountBootstrap(string accessToken)
    {
        http = new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"), Timeout = TimeSpan.FromMinutes(1) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<string> ResolveAccountIdAsync(string? requestedAccountId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedAccountId)) return requestedAccountId.Trim();
        using var response = await http.GetAsync("accounts?per_page=50", ct);
        using var result = await ReadResultAsync(response, "Cloudflare account list failed.", ct);
        var accounts = result.RootElement.EnumerateArray().Select(x => new
        {
            Id = x.GetProperty("id").GetString() ?? "",
            Name = x.TryGetProperty("name", out var name) ? name.GetString() ?? "Cloudflare account" : "Cloudflare account",
        }).Where(x => x.Id.Length > 0).ToList();
        if (accounts.Count == 1) return accounts[0].Id;
        if (accounts.Count == 0) throw new InvalidOperationException("Cloudflare account was not found.");
        throw new InvalidOperationException("Multiple Cloudflare accounts are available. Select the account once during setup.");
    }

    public async Task EnsureWorkersSubdomainAsync(string accountId, string installationId, CancellationToken ct)
    {
        var path = $"accounts/{Uri.EscapeDataString(accountId)}/workers/subdomain";
        using (var existing = await http.GetAsync(path, ct))
        {
            if (existing.IsSuccessStatusCode)
            {
                using var result = await ReadResultAsync(existing, "workers.dev inspection failed.", ct);
                if (result.RootElement.TryGetProperty("subdomain", out var current) && !string.IsNullOrWhiteSpace(current.GetString())) return;
            }
            else if (existing.StatusCode != HttpStatusCode.NotFound)
            {
                var text = await existing.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"workers.dev inspection failed. HTTP {(int)existing.StatusCode}: {text}");
            }
        }

        var marker = new string(installationId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (marker.Length < 20) marker = marker.PadRight(20, '0');
        var subdomain = "cloudflarebox-" + marker[..20];
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(new { subdomain }, options: SettingsStore.Options),
        };
        using var created = await http.SendAsync(request, ct);
        if (!created.IsSuccessStatusCode)
        {
            var text = await created.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"workers.dev subdomain creation failed. HTTP {(int)created.StatusCode}: {text}");
        }
    }

    private static async Task<JsonDocument> ReadResultAsync(HttpResponseMessage response, string message, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{message} HTTP {(int)response.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
        using var envelope = JsonDocument.Parse(bytes);
        if (!envelope.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean() || !envelope.RootElement.TryGetProperty("result", out var result))
            throw new InvalidDataException(message);
        return JsonDocument.Parse(result.GetRawText());
    }

    public void Dispose() => http.Dispose();
}
