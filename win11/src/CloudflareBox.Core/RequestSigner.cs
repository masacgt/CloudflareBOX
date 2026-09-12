using System.Security.Cryptography;
using System.Text;

namespace CloudflareBox.Core;

public sealed class RequestSigner(RSA signingKey, string deviceId)
{
    public void Apply(HttpRequestMessage request, ReadOnlySpan<byte> body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString();
        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var pathAndQuery = request.RequestUri?.PathAndQuery ?? throw new InvalidOperationException("Request URI is required");
        var canonical = string.Join("\n", request.Method.Method.ToUpperInvariant(), pathAndQuery, bodyHash, timestamp, nonce);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var signature = signingKey.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.Headers.Add("X-CB-Device-Id", deviceId);
        request.Headers.Add("X-CB-Timestamp", timestamp);
        request.Headers.Add("X-CB-Nonce", nonce);
        request.Headers.Add("X-CB-Signature", Convert.ToBase64String(signature));
    }
}
