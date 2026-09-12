using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudflareBox.Service;

internal static class CloudflareOAuth
{
    private const string AuthorizeEndpoint = "https://dash.cloudflare.com/oauth2/auth";
    private const string TokenEndpoint = "https://dash.cloudflare.com/oauth2/token";
    private const string RevokeEndpoint = "https://dash.cloudflare.com/oauth2/revoke";
    private const string DefaultRedirect = "http://127.0.0.1:53682/oauth/callback/";
    private const string DefaultScopes = "account-settings.read workers-scripts.write workers-r2.write d1.write";

    private sealed record TokenResponse(string access_token, string? refresh_token, int expires_in, string? scope, string? token_type);

    public static async Task<CloudflareToken> ConnectAsync(string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Cloudflare OAuth client ID is required.", nameof(clientId));
        var redirect = Environment.GetEnvironmentVariable("CLOUDFLAREBOX_OAUTH_REDIRECT") ?? DefaultRedirect;
        var scopes = Environment.GetEnvironmentVariable("CLOUDFLAREBOX_OAUTH_SCOPES") ?? DefaultScopes;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        var authUrl = AuthorizeEndpoint + "?" + Form(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["scope"] = scopes,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        var contextTask = listener.GetContextAsync();
        var completed = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5), ct));
        if (completed != contextTask) throw new TimeoutException("Cloudflare authorization timed out.");
        var context = await contextTask;
        var query = context.Request.QueryString;
        var responseText = query["error"] is null ? "CloudflareBOXとの連携が完了しました。この画面を閉じてください。" : "CloudflareBOXとの連携を完了できませんでした。アプリに戻ってください。";
        var responseBytes = Encoding.UTF8.GetBytes(responseText);
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, ct);
        context.Response.Close();

        if (!string.Equals(query["state"], state, StringComparison.Ordinal)) throw new InvalidOperationException("OAuth state mismatch.");
        if (query["error"] is string error)
        {
            var description = query["error_description"];
            var detail = string.IsNullOrWhiteSpace(description) ? error : $"{error}: {description}";
            throw new InvalidOperationException($"Cloudflare authorization failed: {detail} Requested scopes: {scopes}");
        }
        var code = query["code"] ?? throw new InvalidOperationException("Cloudflare did not return an authorization code.");
        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        }, clientId, ct);
        SaveToken(token);
        return token;
    }

    public static async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var token = LoadToken();
        if (token.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120) return token.AccessToken;
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Cloudflare authorization expired. Reconnect Cloudflare.");
        var refreshed = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = token.ClientId,
            ["refresh_token"] = token.RefreshToken!,
        }, token.ClientId, ct);
        if (string.IsNullOrWhiteSpace(refreshed.RefreshToken)) refreshed = refreshed with { RefreshToken = token.RefreshToken };
        SaveToken(refreshed);
        return refreshed.AccessToken;
    }

    public static async Task RevokeAsync(CancellationToken ct)
    {
        if (!File.Exists(AppPaths.OAuthToken)) return;
        var token = LoadToken();
        try
        {
            using var http = new HttpClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token.AccessToken, ["client_id"] = token.ClientId });
            using var response = await http.PostAsync(RevokeEndpoint, content, ct);
        }
        finally
        {
            File.Delete(AppPaths.OAuthToken);
        }
    }

    private static async Task<CloudflareToken> ExchangeAsync(Dictionary<string, string> form, string clientId, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Cloudflare OAuth token exchange failed ({(int)response.StatusCode}): {Encoding.UTF8.GetString(bytes)}");
        var raw = JsonSerializer.Deserialize<TokenResponse>(bytes, SettingsStore.Options) ?? throw new InvalidDataException("Invalid Cloudflare OAuth response.");
        if (string.IsNullOrWhiteSpace(raw.access_token)) throw new InvalidDataException("Cloudflare OAuth response did not contain an access token.");
        return new CloudflareToken(raw.access_token, raw.refresh_token, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, raw.expires_in), raw.scope ?? "", clientId);
    }

    private static void SaveToken(CloudflareToken token)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(token, SettingsStore.Options);
        try { WindowsKeyStore.Save(AppPaths.OAuthToken, plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static CloudflareToken LoadToken()
    {
        if (!File.Exists(AppPaths.OAuthToken)) throw new InvalidOperationException("Cloudflare is not connected.");
        var plain = WindowsKeyStore.Load(AppPaths.OAuthToken);
        try { return JsonSerializer.Deserialize<CloudflareToken>(plain, SettingsStore.Options) ?? throw new InvalidDataException("Invalid OAuth token store."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Form(IReadOnlyDictionary<string, string> values) => string.Join('&', values.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}
