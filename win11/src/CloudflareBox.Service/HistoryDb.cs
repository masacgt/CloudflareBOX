using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CloudflareBox.Service;

internal sealed class HistoryDb : IDisposable
{
    private readonly IntPtr db;

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr value);

    public HistoryDb(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rc = sqlite3_open_v2(path, out db, 0x2 | 0x4, IntPtr.Zero);
        if (rc != 0) throw new Win32Exception(rc, "Could not open SQLite history database");
        Execute("CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, transfer_id TEXT NOT NULL, saved_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, sha256 TEXT NOT NULL, duplicate_skipped INTEGER NOT NULL, completed_at INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS history_completed_idx ON history(completed_at DESC);");
    }

    public void Record(string transferId, string savedPath, long sizeBytes, string sha256, bool duplicateSkipped)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Execute($"INSERT INTO history(transfer_id,saved_path,size_bytes,sha256,duplicate_skipped,completed_at) VALUES({Q(transferId)},{Q(savedPath)},{sizeBytes},{Q(sha256)},{(duplicateSkipped ? 1 : 0)},{now});");
    }

    private void Execute(string sql)
    {
        var rc = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error);
        try
        {
            if (rc != 0) throw new InvalidOperationException(error == IntPtr.Zero ? $"SQLite error {rc}" : Marshal.PtrToStringUTF8(error));
        }
        finally
        {
            if (error != IntPtr.Zero) sqlite3_free(error);
        }
    }

    public void Dispose() => sqlite3_close(db);
}
