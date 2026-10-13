using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Threading;

namespace ProxyTrafficFloat;

public sealed class Settings
{
    public string Language { get; set; } = "auto";
    public string ColorMode { get; set; } = "system";
    public bool Visible { get; set; } = true;
    public bool ShowSpeed { get; set; } = true;
    public bool ShowDownload { get; set; } = true;
    public bool ShowUpload { get; set; } = true;
    public bool ShowTotal { get; set; } = true;
    public bool ShowQuotaForecast { get; set; }
    public double QuotaGB { get; set; }
    public long QuotaBaselineBytes { get; set; }
    public DateTime QuotaUpdatedUtc { get; set; }
    [JsonIgnore]
    public bool HasQuota => QuotaUpdatedUtc != default;
    public bool ShowTargetTotal { get; set; }
    public bool RecordTimeline { get; set; }
    public bool TimelinePeaks { get; set; } = true;
    public int TimelineRecentMinutes { get; set; } = 10;
    public int TimelineMetric { get; set; }
    public int TimelineRange { get; set; }
    public bool SingleConnection { get; set; }
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool LockClickThrough { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool SnapToEdges { get; set; } = true;
    public bool MiniDockCorner { get; set; }
    public bool MiniHasAnchor { get; set; }
    public string MiniMonitorName { get; set; } = "";
    public double MiniAnchorX { get; set; } = 1;
    public double MiniAnchorY { get; set; } = 1;
    public bool AutoStart { get; set; }
    public double Opacity { get; set; } = 0.92;
    public bool MiniMode { get; set; }
    public bool AutoExpand { get; set; } = true;
    public int Corner { get; set; }
    public double DownloadThresholdKB { get; set; } = 1024;
    public double UploadThresholdKB { get; set; } = 512;
    public double TriggerSeconds { get; set; } = 2;
    public double QuietSeconds { get; set; } = 10;
    public double UiScale { get; set; } = 1;
    public string MonitorName { get; set; } = "";
    public double AnchorX { get; set; } = 0.05;
    public double AnchorY { get; set; } = 0.1;
    public bool HasAnchor { get; set; }
    public double Left { get; set; } = 32;
    public double Top { get; set; } = 100;
    public string Endpoint { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";

    [JsonIgnore]
    public string Secret
    {
        get => WindowsCrypto.Unprotect(ProtectedSecret);
        set => ProtectedSecret = WindowsCrypto.Protect(value);
    }

    [JsonIgnore]
    public DateTime ModifiedUtc { get; private set; }
    [JsonIgnore]
    public bool Recovered { get; private set; }
    public Settings Copy() => (Settings)MemberwiseClone();

    // Both readers and read-modify-write operations cooperate on the same per-file mutex.
    static T WithFileLock<T>(string path, Func<T> action)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16];
        using var mutex = new Mutex(false, @"Local\TrafficLens.Settings." + key);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { } // The next owner can recover the last atomic file.
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    public static Settings Load(string path) => WithFileLock(path, () => Read(path));

    public static Settings Update(string path, Action<Settings> change) => WithFileLock(path, () =>
    {
        var latest = Read(path);
        change(latest);
        latest.Write(path);
        return latest;
    });

    static Settings Read(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.Settings) ?? throw new JsonException("Expected settings object.");
            settings.Language = Strings.NormalizeLanguage(settings.Language);
            settings.ColorMode = AppTheme.Normalize(settings.ColorMode);
            // Settings can be edited outside the app. Bound values before they reach window geometry or timers.
            settings.QuotaGB = Clamp(settings.QuotaGB, 0, 1048576, 0);
            settings.Opacity = Clamp(settings.Opacity, 0.35, 1, 0.92);
            settings.UiScale = Clamp(settings.UiScale, 0.8, 1.25, 1);
            settings.Corner = Math.Clamp(settings.Corner, 0, 3);
            settings.TimelineMetric = Math.Clamp(settings.TimelineMetric, 0, 1);
            settings.TimelineRecentMinutes = settings.TimelineRecentMinutes == 60 ? 60 : 10;
            settings.TimelineRange = Math.Clamp(settings.TimelineRange, 0, 2);
            settings.DownloadThresholdKB = Clamp(settings.DownloadThresholdKB, 1, 1048576, 1024);
            settings.UploadThresholdKB = Clamp(settings.UploadThresholdKB, 1, 1048576, 512);
            settings.TriggerSeconds = Clamp(settings.TriggerSeconds, 1, 30, 2);
            settings.QuietSeconds = Clamp(settings.QuietSeconds, 3, 120, 10);
            settings.AnchorX = Clamp(settings.AnchorX, 0, 1, 0.05);
            settings.AnchorY = Clamp(settings.AnchorY, 0, 1, 0.1);
            settings.MiniAnchorX = Clamp(settings.MiniAnchorX, 0, 1, 1);
            settings.MiniAnchorY = Clamp(settings.MiniAnchorY, 0, 1, 1);
            settings.ModifiedUtc = File.GetLastWriteTimeUtc(path);
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException error)
        {
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Diagnostics.Write(Path.GetDirectoryName(path)!, "settings-corrupt", error);
            return new() { Recovered = true };
        }
    }

    static double Clamp(double value, double minimum, double maximum, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);

    // Complete snapshots use Save; concurrent field edits use Update.
    public void Save(string path) => WithFileLock(path, () => { Write(path); return this; });
    void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // The resident monitor and settings host can save at the same time.
        string temporary = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
        File.Move(temporary, path, true);
        ModifiedUtc = File.GetLastWriteTimeUtc(path);
    }
}

public static class WindowsCrypto
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr pointer);

    static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    public static string Protect(string value) =>
        value.Length == 0 ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));

    public static string Unprotect(string? value)
    {
        // A secret encrypted by another Windows account is intentionally treated as unavailable.
        try
        {
            return string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
        }
        catch (Exception error) when (error is FormatException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return "";
        }
    }
}

public record DailyRow(string Target, long Download, long Upload);

public sealed partial class TrafficStore : IDisposable
{
    IntPtr db;

    // Windows' bundled winsqlite3 uses stdcall, including callbacks; x86 requires an exact ABI match.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Callback(IntPtr context, int count, IntPtr values, IntPtr names);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_close(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        Callback? callback, IntPtr context, out IntPtr error);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern void sqlite3_free(IntPtr pointer);

    public TrafficStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            if (sqlite3_open(path, out db) != 0) throw new IOException("无法打开统计数据库");
            Exec("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=2000; " +
                "CREATE TABLE IF NOT EXISTS daily(day TEXT NOT NULL,target TEXT NOT NULL," +
                "download INTEGER NOT NULL,upload INTEGER NOT NULL,PRIMARY KEY(day,target));" +
                "CREATE TABLE IF NOT EXISTS usage_counter(id INTEGER PRIMARY KEY,bytes INTEGER NOT NULL);" +
                "INSERT OR IGNORE INTO usage_counter SELECT 1,COALESCE((SELECT SUM(download+upload) FROM daily),0) WHERE NOT EXISTS(SELECT 1 FROM usage_counter WHERE id=1);" +
                "CREATE TABLE IF NOT EXISTS timeline(minute INTEGER NOT NULL,target TEXT NOT NULL," +
                "download INTEGER NOT NULL,upload INTEGER NOT NULL,seconds REAL NOT NULL,first INTEGER NOT NULL,last INTEGER NOT NULL," +
                "PRIMARY KEY(minute,target));" +
                "CREATE TABLE IF NOT EXISTS timeline_names(id INTEGER PRIMARY KEY,target TEXT NOT NULL UNIQUE);" +
                "CREATE TABLE IF NOT EXISTS timeline_seconds(stamp INTEGER PRIMARY KEY,download INTEGER NOT NULL,upload INTEGER NOT NULL,seconds REAL NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS timeline_target_seconds(stamp INTEGER NOT NULL,target_id INTEGER NOT NULL,download INTEGER NOT NULL,upload INTEGER NOT NULL,PRIMARY KEY(stamp,target_id)) WITHOUT ROWID;" +
                "CREATE INDEX IF NOT EXISTS timeline_target_time ON timeline_target_seconds(target_id,stamp);");
            Prune(DateTime.Today);
            PruneTimeline(DateTimeOffset.UtcNow);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    void Exec(string sql, Callback? callback = null)
    {
        if (sqlite3_exec(db, sql, callback, IntPtr.Zero, out var error) == 0) return;
        string message = Marshal.PtrToStringUTF8(error) ?? "SQLite 错误";
        sqlite3_free(error);
        throw new IOException(message);
    }

    static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    public void Add(IEnumerable<Increment> increments)
    {
        var rows = new Dictionary<(DateTime Day, string Target), (long Download, long Upload)>();
        foreach (var increment in increments)
        {
            if (increment.Download == 0 && increment.Upload == 0) continue;
            var key = (increment.Day.Date, increment.Target);
            rows.TryGetValue(key, out var total);
            rows[key] = (checked(total.Download + increment.Download), checked(total.Upload + increment.Upload));
        }
        if (rows.Count == 0) return;

        long observed = 0;
        var sql = new StringBuilder("BEGIN IMMEDIATE;");
        foreach (var row in rows)
        {
            observed = checked(observed + row.Value.Download + row.Value.Upload);
            string day = Quote(row.Key.Day.ToString("yyyy-MM-dd")), target = Quote(row.Key.Target);
            // INSERT OR IGNORE + UPDATE also works with SQLite versions shipped in older Windows 10.
            sql.Append($"INSERT OR IGNORE INTO daily VALUES({day},{target},0,0);");
            sql.Append($"UPDATE daily SET download=download+{row.Value.Download},upload=upload+{row.Value.Upload} " +
                $"WHERE day={day} AND target={target};");
        }
        // One bounded counter shares the daily transaction and survives history pruning/clearing.
        sql.Append($"UPDATE usage_counter SET bytes=bytes+{observed} WHERE id=1;COMMIT;");
        Transaction(sql.ToString());
    }

    void Transaction(string sql)
    {
        try { Exec(sql); }
        catch
        {
            try { Exec("ROLLBACK;"); } catch (IOException) { }
            throw;
        }
    }

    public List<DailyRow> Query(DateTime first, DateTime last)
    {
        var rows = new List<DailyRow>();
        Callback callback = (_, _, values, _) =>
        {
            // Exceptions must not escape a native SQLite callback.
            try
            {
                string Cell(int index) => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, index * IntPtr.Size)) ?? "0";
                rows.Add(new(Cell(0), long.Parse(Cell(1)), long.Parse(Cell(2))));
                return 0;
            }
            catch { return 1; }
        };
        Exec($"SELECT target,SUM(download),SUM(upload) FROM daily WHERE day>={Quote(first.ToString("yyyy-MM-dd"))} " +
            $"AND day<={Quote(last.ToString("yyyy-MM-dd"))} GROUP BY target ORDER BY SUM(download)+SUM(upload) DESC;", callback);
        GC.KeepAlive(callback);
        return rows;
    }

    public long ObservedBytes()
    {
        long bytes = 0;
        Callback callback = (_, _, values, _) =>
            long.TryParse(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values)), out bytes) ? 0 : 1;
        Exec("SELECT bytes FROM usage_counter WHERE id=1;", callback);
        GC.KeepAlive(callback);
        return bytes;
    }

    public UsageTrend RecentDailyTrend(DateTime today)
    {
        UsageTrend trend = default;
        Callback callback = (_, _, values, _) =>
        {
            string average = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values)) ?? "0";
            string count = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, IntPtr.Size)) ?? "0";
            if (!double.TryParse(average, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bytes) || !int.TryParse(count, out var days)) return 1;
            trend = new(bytes, days);
            return 0;
        };
        // Exclude the unfinished current day; at most seven recorded days within retained history.
        Exec($"SELECT COALESCE(AVG(bytes),0),COUNT(*) FROM (SELECT SUM(download+upload) AS bytes FROM daily " +
            $"WHERE day<{Quote(today.Date.ToString("yyyy-MM-dd"))} GROUP BY day HAVING bytes>0 ORDER BY day DESC LIMIT 7);", callback);
        GC.KeepAlive(callback);
        return trend;
    }

    public Dictionary<string, Amount> Today() =>
        Query(DateTime.Today, DateTime.Today).ToDictionary(row => row.Target, row => new Amount(row.Download, row.Upload));

    public void Prune(DateTime today) => Exec($"DELETE FROM daily WHERE day<{Quote(today.Date.AddDays(-29).ToString("yyyy-MM-dd"))};");
    public void Clear() => Transaction("BEGIN IMMEDIATE;DELETE FROM daily;DELETE FROM timeline;DELETE FROM timeline_target_seconds;DELETE FROM timeline_seconds;DELETE FROM timeline_names;COMMIT;");

    public void Dispose()
    {
        if (db == IntPtr.Zero) return;
        sqlite3_close(db);
        db = IntPtr.Zero;
    }
}
