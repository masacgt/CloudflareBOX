using System.Net;
using System.Net.Http.Headers;

namespace CloudflareBox.Core;

public static class ResumableDownloader
{
    public static async Task DownloadAsync(HttpClient http, Uri url, string targetPath, long expectedLength, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var existing = File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0;
        if (existing > expectedLength) { File.Delete(targetPath); existing = 0; }
        if (existing == expectedLength) return;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existing > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(targetPath);
            existing = 0;
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        await using var output = new FileStream(targetPath, existing == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await input.CopyToAsync(output, 1024 * 1024, ct);
        await output.FlushAsync(ct);
        if (output.Length != expectedLength) throw new IOException($"Encrypted download length mismatch: {output.Length} != {expectedLength}");
    }
}
