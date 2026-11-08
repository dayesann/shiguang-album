using System.Runtime.InteropServices;
using System.Text;

namespace Shiguang;

// Windows 10+ ships this SQLite library. No NuGet or separate native DLL is needed.
public sealed class NativeDb : IDisposable
{
    nint db;
    readonly object gate = new();
    const string Dll = "winsqlite3.dll";
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out nint db, int flags, nint vfs);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(nint db);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(nint db, int ms);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern nint sqlite3_errmsg(nint db);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(nint db, byte[] sql, int length, out nint stmt, nint tail);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(nint stmt);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(nint stmt);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_text(nint stmt, int index, byte[] value, int length, nint destructor);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_int64(nint stmt, int index, long value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_double(nint stmt, int index, double value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_null(nint stmt, int index);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(nint stmt);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern nint sqlite3_column_text(nint stmt, int column);

    public NativeDb(string path, bool readOnly = false)
    {
        if (sqlite3_open_v2(path, out db, (readOnly ? 1 : 2 | 4) | 0x10000, 0) != 0) { if (db != 0) sqlite3_close(db); db = 0; throw new IOException("无法打开照片库数据库"); }
        sqlite3_busy_timeout(db, 5000);
        if (readOnly) return;
        Run("PRAGMA foreign_keys=ON");
        Run("PRAGMA journal_mode=WAL");
        Run("PRAGMA synchronous=FULL");
    }
    public List<string[]> Query(string sql, params object?[] args)
    {
        lock (gate)
        {
            var bytes = Encoding.UTF8.GetBytes(sql + "\0");
            Check(sqlite3_prepare_v2(db, bytes, -1, out var stmt, 0));
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    int result;
                    if (args[i] is null) result = sqlite3_bind_null(stmt, i + 1);
                    else if (args[i] is bool b) result = sqlite3_bind_int64(stmt, i + 1, b ? 1 : 0);
                    else if (args[i] is int or long) result = sqlite3_bind_int64(stmt, i + 1, Convert.ToInt64(args[i]));
                    else if (args[i] is float or double) result = sqlite3_bind_double(stmt, i + 1, Convert.ToDouble(args[i]));
                    else { var text = Encoding.UTF8.GetBytes(args[i]!.ToString()! + "\0"); result = sqlite3_bind_text(stmt, i + 1, text, text.Length - 1, new nint(-1)); }
                    Check(result);
                }
                var rows = new List<string[]>();
                int rc;
                while ((rc = sqlite3_step(stmt)) == 100)
                {
                    var row = new string[sqlite3_column_count(stmt)];
                    for (int j = 0; j < row.Length; j++) row[j] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, j)) ?? "";
                    rows.Add(row);
                }
                if (rc != 101) Check(rc);
                return rows;
            }
            finally { sqlite3_finalize(stmt); }
        }
    }
    public void Run(string sql, params object?[] args) => Query(sql, args);
    public void Transaction(Action action)
    {
        lock (gate)
        {
            Run("BEGIN IMMEDIATE");
            try { action(); Run("COMMIT"); }
            catch { Run("ROLLBACK"); throw; }
        }
    }
    void Check(int rc) { if (rc != 0) throw new IOException("照片库数据库：" + Marshal.PtrToStringUTF8(sqlite3_errmsg(db))); }
    public void Dispose() { lock (gate) { if (db != 0) { sqlite3_close(db); db = 0; } } }
}
