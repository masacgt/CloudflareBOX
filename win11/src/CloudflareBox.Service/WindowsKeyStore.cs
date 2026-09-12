using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CloudflareBox.Service;

internal static class WindowsKeyStore
{
    private const int CryptProtectLocalMachine = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static readonly byte[] Entropy = "CloudflareBOX-v1"u8.ToArray();

    public static void Save(string path, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Protect(data.ToArray()));
    }

    public static byte[] Load(string path) => Unprotect(File.ReadAllBytes(path));

    private static byte[] Protect(byte[] input)
    {
        var inputBlob = ToBlob(input);
        var entropyBlob = ToBlob(Entropy);
        try
        {
            if (!CryptProtectData(ref inputBlob, "CloudflareBOX", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectLocalMachine, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return FromAndFree(output);
        }
        finally
        {
            FreeBlob(inputBlob);
            FreeBlob(entropyBlob);
        }
    }

    private static byte[] Unprotect(byte[] input)
    {
        var inputBlob = ToBlob(input);
        var entropyBlob = ToBlob(Entropy);
        try
        {
            if (!CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return FromAndFree(output);
        }
        finally
        {
            FreeBlob(inputBlob);
            FreeBlob(entropyBlob);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    private static byte[] FromAndFree(DataBlob blob)
    {
        try
        {
            var result = new byte[blob.Size];
            if (blob.Size > 0) Marshal.Copy(blob.Data, result, 0, blob.Size);
            return result;
        }
        finally
        {
            if (blob.Data != IntPtr.Zero) LocalFree(blob.Data);
        }
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero) Marshal.FreeHGlobal(blob.Data);
    }
}
