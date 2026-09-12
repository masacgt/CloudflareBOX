using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CloudflareBox.Core;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var tests = new List<(string Name, Func<Task> Run)>
{
    ("metadata-roundtrip", async () =>
    {
        var key = TransferCrypto.NewFileKey();
        var source = new TransferMetadata("a?.txt", "content://test", "text/plain", 1, 2, new string('a', 64));
        var encrypted = TransferCrypto.EncryptMetadata(source, key);
        var roundtrip = TransferCrypto.DecryptMetadata(Convert.ToBase64String(encrypted.Nonce), Convert.ToBase64String(encrypted.CipherAndTag), key);
        Require(roundtrip == source, "metadata mismatch");
        await Task.CompletedTask;
    }),
    ("frame-roundtrip", async () =>
    {
        var key = TransferCrypto.NewFileKey();
        var source = new byte[] { 1, 2, 3, 4, 5 };
        var combined = TransferCrypto.EncryptFrame(source.AsSpan(0, 3), key)
            .Concat(TransferCrypto.EncryptFrame(source.AsSpan(3, 2), key)).ToArray();
        await using var input = new MemoryStream(combined);
        await using var output = new MemoryStream();
        var hash = await ObjectDecryptor.RunAsync(input, output, 5, 3, 2, key);
        Require(output.ToArray().SequenceEqual(source), "frame plaintext mismatch");
        Require(hash == Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), "frame hash mismatch");
    }),
    ("rsa-key-wrap", async () =>
    {
        using var rsa = RSA.Create(3072);
        var key = TransferCrypto.NewFileKey();
        var wrapped = TransferCrypto.WrapFileKey(key, rsa);
        var unwrapped = TransferCrypto.UnwrapFileKey(Convert.ToBase64String(wrapped), rsa);
        Require(unwrapped.SequenceEqual(key), "RSA OAEP unwrap mismatch");
        await Task.CompletedTask;
    }),
    ("request-signature", async () =>
    {
        using var rsa = RSA.Create(2048);
        var body = System.Text.Encoding.UTF8.GetBytes("{\"x\":1}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/v1/test?q=1");
        new RequestSigner(rsa, "device-1").Apply(request, body);
        var timestamp = request.Headers.GetValues("X-CB-Timestamp").Single();
        var nonce = request.Headers.GetValues("X-CB-Nonce").Single();
        var signature = Convert.FromBase64String(request.Headers.GetValues("X-CB-Signature").Single());
        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var canonical = string.Join("\n", "POST", "/api/v1/test?q=1", bodyHash, timestamp, nonce);
        Require(rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(canonical), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "request signature mismatch");
        await Task.CompletedTask;
    }),
    ("zero-byte-frame", async () =>
    {
        var key = TransferCrypto.NewFileKey();
        var frame = TransferCrypto.EncryptFrame(ReadOnlySpan<byte>.Empty, key);
        Require(frame.Length == TransferCrypto.FrameOverhead, "zero-byte frame length mismatch");
        await using var input = new MemoryStream(frame);
        await using var output = new MemoryStream();
        var hash = await ObjectDecryptor.RunAsync(input, output, 0, 0, 1, key);
        Require(output.Length == 0, "zero-byte plaintext mismatch");
        Require(hash == Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(), "zero-byte hash mismatch");
    }),
    ("filename", async () =>
    {
        Require(FilenamePolicy.Sanitize("CON.txt").StartsWith("_"), "reserved Windows name not sanitized");
        Require(FilenamePolicy.Sanitize(new string('x', 300) + ".txt").Length <= 180, "long filename not shortened");
        await Task.CompletedTask;
    }),
    ("range-resume-success", async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"cloudflarebox-range-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0, 1, 2, 3 });
            using var handler = new StubHandler(_ =>
            {
                var content = new ByteArrayContent(new byte[] { 4, 5, 6, 7 });
                content.Headers.ContentRange = new ContentRangeHeaderValue(4, 7, 8);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            });
            using var rsa = RSA.Create(2048);
            using var client = new CloudflareBoxApiClient("https://example.test/api/v1/", handler);
            client.ConfigureDevice(rsa, "windows-1");
            await client.DownloadEncryptedAsync("transfer-1", path, 8);

            Require(handler.LastRangeFrom == 4, "resume request did not start at local file length");
            Require((await File.ReadAllBytesAsync(path)).SequenceEqual(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }), "resume output mismatch");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }),
    ("range-resume-rejects-mismatched-content-range", async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"cloudflarebox-range-bad-{Guid.NewGuid():N}.bin");
        try
        {
            var prefix = new byte[] { 0, 1, 2, 3 };
            await File.WriteAllBytesAsync(path, prefix);
            using var handler = new StubHandler(_ =>
            {
                var content = new ByteArrayContent(new byte[] { 4, 5, 6, 7 });
                content.Headers.ContentRange = new ContentRangeHeaderValue(3, 6, 8);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            });
            using var rsa = RSA.Create(2048);
            using var client = new CloudflareBoxApiClient("https://example.test/api/v1/", handler);
            client.ConfigureDevice(rsa, "windows-1");

            var rejected = false;
            try
            {
                await client.DownloadEncryptedAsync("transfer-1", path, 8);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Require(rejected, "mismatched Content-Range was accepted");
            Require((await File.ReadAllBytesAsync(path)).SequenceEqual(prefix), "partial file changed after invalid resume response");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    })
};

var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
return failed == 0 ? 0 : 1;

sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        this.responseFactory = responseFactory;
    }

    public long? LastRangeFrom { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRangeFrom = request.Headers.Range?.Ranges.SingleOrDefault()?.From;
        return Task.FromResult(responseFactory(request));
    }
}
