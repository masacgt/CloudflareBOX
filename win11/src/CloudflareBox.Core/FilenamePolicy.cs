using System.Security.Cryptography;

namespace CloudflareBox.Core;

public static class FilenamePolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    public static string Sanitize(string original, int maxLength = 180)
    {
        var name = Path.GetFileName(original);
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var stem = Path.GetFileNameWithoutExtension(name);
        if (Reserved.Contains(stem)) name = "_" + name;
        if (name.Length <= maxLength) return name;
        var ext = Path.GetExtension(name);
        var keep = Math.Max(1, maxLength - ext.Length - 9);
        var suffix = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))).Substring(0, 8).ToLowerInvariant();
        return name[..Math.Min(keep, name.Length)] + "-" + suffix + ext;
    }

    public static string FindAvailablePath(string directory, string requestedName)
    {
        Directory.CreateDirectory(directory);
        var safe = Sanitize(requestedName);
        var candidate = Path.Combine(directory, safe);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(safe);
        var ext = Path.GetExtension(safe);
        for (var i = 1; i < 100_000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate a unique destination filename");
    }
}
