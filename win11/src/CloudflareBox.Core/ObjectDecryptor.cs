using System.Security.Cryptography;

namespace CloudflareBox.Core;

public static class ObjectDecryptor
{
    public static async Task<string> RunAsync(Stream input, Stream output, long plainSize, long partSize, int partCount, byte[] key, CancellationToken cancellationToken = default)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long remaining = plainSize;
        for (var index = 0; index < partCount; index++)
        {
            var plainLength = plainSize == 0 ? 0 : checked((int)Math.Min(partSize, remaining));
            var frame = new byte[TransferCrypto.FrameOverhead + plainLength];
            await ReadFrameAsync(input, frame, cancellationToken);
            if (frame[0] != TransferCrypto.FrameVersion) throw new InvalidDataException("Unsupported encrypted frame version");
            var plain = new byte[plainLength];
            using var aes = new AesGcm(key, TransferCrypto.TagSize);
            aes.Decrypt(
                frame.AsSpan(1, TransferCrypto.NonceSize),
                frame.AsSpan(1 + TransferCrypto.NonceSize, plainLength),
                frame.AsSpan(frame.Length - TransferCrypto.TagSize, TransferCrypto.TagSize),
                plain);
            digest.AppendData(plain);
            await output.WriteAsync(plain, cancellationToken);
            remaining -= plainLength;
        }
        if (remaining != 0) throw new InvalidDataException("Encrypted object size does not match transfer metadata");
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task ReadFrameAsync(Stream input, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await input.ReadAsync(destination[offset..], cancellationToken);
            if (read == 0) throw new EndOfStreamException("Encrypted object ended early");
            offset += read;
        }
    }
}
