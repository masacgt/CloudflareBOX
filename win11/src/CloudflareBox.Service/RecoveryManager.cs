using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudflareBox.Service;

internal static class RecoveryManager
{
    private const int Iterations = 600_000;
    private const int TagBytes = 16;
    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private sealed record RecoveryPayload(string SigningPrivateKeyB64, string EncryptionPrivateKeyB64, StoredSettings Settings);
    private sealed record RecoveryEnvelope(int Version, int Iterations, string SaltB64, string NonceB64, string CipherB64, string TagB64);

    public static string GenerateRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var chars = new char[20];
        for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return string.Join('-', Enumerable.Range(0, 5).Select(i => new string(chars, i * 4, 4)));
    }

    public static void Export(string path, string recoveryCode)
    {
        ValidateCode(recoveryCode);
        if (!File.Exists(AppPaths.Settings) || !File.Exists(AppPaths.SigningKey) || !File.Exists(AppPaths.EncryptionKey))
            throw new InvalidOperationException("CloudflareBOX is not initialized.");

        var settings = SettingsStore.Load<StoredSettings>(AppPaths.Settings);
        var signing = WindowsKeyStore.Load(AppPaths.SigningKey);
        var encryption = WindowsKeyStore.Load(AppPaths.EncryptionKey);
        try
        {
            var payload = new RecoveryPayload(Convert.ToBase64String(signing), Convert.ToBase64String(encryption), settings);
            var plain = JsonSerializer.SerializeToUtf8Bytes(payload, SettingsStore.Options);
            var salt = RandomNumberGenerator.GetBytes(16);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var key = Rfc2898DeriveBytes.Pbkdf2(NormalizeCode(recoveryCode), salt, Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                var cipher = new byte[plain.Length];
                var tag = new byte[TagBytes];
                using var aes = new AesGcm(key, TagBytes);
                aes.Encrypt(nonce, plain, cipher, tag, "CloudflareBOX-Recovery-v1"u8);
                var envelope = new RecoveryEnvelope(1, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope, SettingsStore.Options));
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(cipher);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signing);
            CryptographicOperations.ZeroMemory(encryption);
        }
    }

    public static void Import(string path, string recoveryCode)
    {
        ValidateCode(recoveryCode);
        var envelope = JsonSerializer.Deserialize<RecoveryEnvelope>(File.ReadAllBytes(path), SettingsStore.Options)
            ?? throw new InvalidDataException("Invalid recovery bundle.");
        if (envelope.Version != 1 || envelope.Iterations < 100_000) throw new InvalidDataException("Unsupported recovery bundle.");

        var salt = Convert.FromBase64String(envelope.SaltB64);
        var nonce = Convert.FromBase64String(envelope.NonceB64);
        var cipher = Convert.FromBase64String(envelope.CipherB64);
        var tag = Convert.FromBase64String(envelope.TagB64);
        var key = Rfc2898DeriveBytes.Pbkdf2(NormalizeCode(recoveryCode), salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            try { aes.Decrypt(nonce, cipher, tag, plain, "CloudflareBOX-Recovery-v1"u8); }
            catch (CryptographicException) { throw new InvalidDataException("Recovery code is incorrect or the recovery bundle is damaged."); }
            var payload = JsonSerializer.Deserialize<RecoveryPayload>(plain, SettingsStore.Options)
                ?? throw new InvalidDataException("Invalid recovery payload.");
            var signing = Convert.FromBase64String(payload.SigningPrivateKeyB64);
            var encryption = Convert.FromBase64String(payload.EncryptionPrivateKeyB64);
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                WindowsKeyStore.Save(AppPaths.SigningKey, signing);
                WindowsKeyStore.Save(AppPaths.EncryptionKey, encryption);
                SettingsStore.Save(AppPaths.Settings, payload.Settings);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signing);
                CryptographicOperations.ZeroMemory(encryption);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    private static string NormalizeCode(string code) => new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static void ValidateCode(string code)
    {
        if (NormalizeCode(code).Length < 16) throw new ArgumentException("Recovery code is too short.", nameof(code));
    }
}
