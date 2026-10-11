using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ProxyTrafficFloat;

public static class StorageTests
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        IntPtr callback, IntPtr context, out IntPtr error);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_close(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern void sqlite3_free(IntPtr pointer);

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "ProxyTrafficFloat-rollback-" + Guid.NewGuid() + ".sqlite");
        try
        {
            using var store = new TrafficStore(path);
            if (sqlite3_open(path, out var database) != 0) throw new Exception("Cannot open rollback fixture");
            try
            {
                int result = sqlite3_exec(database,
                    "CREATE TRIGGER reject_fixture BEFORE UPDATE ON daily WHEN NEW.target='reject.test' " +
                    "BEGIN SELECT RAISE(ABORT,'fixture failure'); END;", IntPtr.Zero, IntPtr.Zero, out var error);
                if (error != IntPtr.Zero) sqlite3_free(error);
                if (result != 0) throw new Exception("Cannot create rollback fixture");
            }
            finally { sqlite3_close(database); }

            bool failed = false;
            try
            {
                store.Add(new[]
                {
                    new Increment(DateTime.Today, "a'中文.test", 100, 3),
                    new Increment(DateTime.Today, "reject.test", 200, 4)
                });
            }
            catch (IOException) { failed = true; }
            if (!failed || store.Today().Count != 0) throw new Exception("Partial daily totals survived a failed transaction");

            store.Add(new[] { new Increment(DateTime.Today, "a'中文.test", 7, 2) });
            var saved = store.Today().Single();
            if (saved.Key != "a'中文.test" || saved.Value.Download != 7 || saved.Value.Upload != 2)
                throw new Exception("SQLite connection did not recover after rollback");
            Console.WriteLine("PASS: transaction rollback leaves no partial totals and next write recovers");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
