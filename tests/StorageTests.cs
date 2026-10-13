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

    public static void Execute(string path, string sql)
    {
        if(sqlite3_open(path,out var database)!=0) throw new Exception("Cannot open fixture database.");
        try
        {
            int result=sqlite3_exec(database,sql,IntPtr.Zero,IntPtr.Zero,out var error);
            if(error!=IntPtr.Zero) sqlite3_free(error);
            if(result!=0) throw new Exception("Cannot write SQL fixture.");
        }
        finally { sqlite3_close(database); }
    }
    static void ReadOnlyDoesNotMaintainHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrafficLens-readonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "live.sqlite");
        try
        {
            using (var writer = new TrafficStore(path))
                writer.Add(new[] { new Increment(DateTime.Today, "live.test", 7, 2) });
            Execute(path, "DROP TABLE usage_counter; INSERT INTO daily VALUES('2000-01-01','old.test',100,20);" +
                "INSERT INTO timeline_seconds VALUES(1,5,1,1);" +
                "CREATE TRIGGER protect_daily BEFORE DELETE ON daily BEGIN SELECT RAISE(ABORT,'reader pruned daily'); END;" +
                "CREATE TRIGGER protect_seconds BEFORE DELETE ON timeline_seconds BEGIN SELECT RAISE(ABORT,'reader pruned timeline'); END;");
            byte[] before = File.ReadAllBytes(path);
            using (var reader = TrafficStore.OpenReadOnly(path))
            {
                if (reader.Query(new DateTime(2000, 1, 1), DateTime.Today).Count != 2)
                    throw new Exception("Read-only query lost retained fixture rows.");
                reader.ExportCsv(Path.Combine(root, "daily.csv"), false);
                reader.ExportCsv(Path.Combine(root, "timeline.csv"), true);
                reader.Backup(Path.Combine(root, "backup.sqlite"));
            }
            if (!before.SequenceEqual(File.ReadAllBytes(path)))
                throw new Exception("Read-only query/export/backup modified source database bytes.");
            if (!File.ReadAllText(Path.Combine(root, "daily.csv")).Contains("old.test") || File.ReadAllLines(Path.Combine(root, "timeline.csv")).Length != 2)
                throw new Exception("Read-only export pruned fixture history.");
            using (var backup = TrafficStore.OpenReadOnly(Path.Combine(root, "backup.sqlite")))
                if (backup.Query(new DateTime(2000, 1, 1), DateTime.Today).Count != 2)
                    throw new Exception("Read-only backup lost committed source history.");
            string writeCheck = Path.Combine(root, "write-check.sqlite");
            using (var writer = new TrafficStore(writeCheck))
                writer.Add(new[] { new Increment(DateTime.Today, "original.test", 3, 1) });
            using (var reader = TrafficStore.OpenReadOnly(writeCheck))
            {
                bool rejected = false;
                try { reader.Add(new[] { new Increment(DateTime.Today, "bad.test", 1, 0) }); }
                catch (IOException) { rejected = true; }
                if (!rejected || reader.Today().Count != 1) throw new Exception("Read-only handle accepted a write against a complete schema.");
            }
            string missing = Path.Combine(root, "missing.sqlite");
            bool missingRejected = false;
            try { using var absent = TrafficStore.OpenReadOnly(missing); } catch (IOException) { missingRejected = true; }
            if (!missingRejected || File.Exists(missing)) throw new Exception("Read-only open created an absent database.");
            bool schemaMissing = false;
            try { Execute(path, "SELECT * FROM usage_counter;"); } catch (Exception) { schemaMissing = true; }
            if (!schemaMissing) throw new Exception("Read-only opening performed schema migration.");
            Console.WriteLine("PASS: read-only queries, CSV and online backup preserve source schema/history/bytes and reject writes");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void Run()
    {
        ReadOnlyDoesNotMaintainHistory();
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
