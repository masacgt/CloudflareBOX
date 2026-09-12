using System.Security.Cryptography;
using System.Text.Json;

namespace CloudflareBox.Core;

public static class TransferCrypto
{
    public const byte FrameVersion = 1;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int FrameOverhead = 1 + NonceSize + TagSize;

    public static byte[] NewFileKey() => RandomNumberGenerator.GetBytes(32);

    public static byte[] WrapFileKey(byte[] fileKey, RSA windowsPublicKey) =>
        windowsPublicKey.Encrypt(fileKey, RSAEncryptionPadding.OaepSHA256);

    public static byte[] UnwrapFileKey(string wrappedKeyB64, RSA windowsPrivateKey) =>
        windowsPrivateKey.Decrypt(Convert.FromBase64String(wrappedKeyB64), RSAEncryptionPadding.OaepSHA256);

    public static (byte[] Nonce, byte[] CipherAndTag) EncryptMetadata(TransferMetadata metadata, byte[] fileKey)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(metadata);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(fileKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return (nonce, [.. cipher, .. tag]);
    }

    public static TransferMetadata DecryptMetadata(string nonceB64, string cipherB64, byte[] fileKey)
    {
        var nonce = Convert.FromBase64String(nonceB64);
        var combined = Convert.FromBase64String(cipherB64);
        if (nonce.Length != NonceSize || combined.Length < TagSize) throw new CryptographicException("Invalid metadata envelope");
        var cipherLength = combined.Length - TagSize;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(fileKey, TagSize);
        aes.Decrypt(nonce, combined.AsSpan(0, cipherLength), combined.AsSpan(cipherLength, TagSize), plain);
        return JsonSerializer.Deserialize<TransferMetadata>(plain) ?? throw new CryptographicException("Metadata JSON is invalid");
    }

    public static byte[] EncryptFrame(ReadOnlySpan<byte> plaintext, byte[] fileKey, byte[]? nonce = null)
    {
        nonce ??= RandomNumberGenerator.GetBytes(NonceSize);
        if (nonce.Length != NonceSize) throw new ArgumentException("Nonce must be 12 bytes", nameof(nonce));
        var output = new byte[FrameOverhead + plaintext.Length];
        output[0] = FrameVersion;
        nonce.CopyTo(output.AsSpan(1, NonceSize));
        using var aes = new AesGcm(fileKey, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(1 + NonceSize, plaintext.Length), output.AsSpan(output.Length - TagSize, TagSize));
        return output;
    }
}
