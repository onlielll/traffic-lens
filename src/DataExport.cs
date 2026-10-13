using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ProxyTrafficFloat;

public sealed partial class TrafficStore
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
    static extern IntPtr sqlite3_backup_init(IntPtr destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, IntPtr source, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_backup_step(IntPtr backup, int pages);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_backup_finish(IntPtr backup);

    // SQLite's online backup includes committed WAL pages; copying only traffic.sqlite would not.
    public void Backup(string filename)
    {
        string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        IntPtr destination = IntPtr.Zero, backup = IntPtr.Zero;
        try
        {
            if (sqlite3_open(temporary, out destination) != 0) throw new IOException("无法创建数据备份");
            backup = sqlite3_backup_init(destination, "main", db, "main");
            if (backup == IntPtr.Zero || sqlite3_backup_step(backup, -1) != 101) throw new IOException("数据备份失败，请重试");
            int result = sqlite3_backup_finish(backup); backup = IntPtr.Zero;
            if (result != 0) throw new IOException("数据备份失败，请重试");
            sqlite3_close(destination); destination = IntPtr.Zero;
            File.Move(temporary, filename, true);
        }
        finally
        {
            if (backup != IntPtr.Zero) sqlite3_backup_finish(backup);
            if (destination != IntPtr.Zero) sqlite3_close(destination);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void ExportCsv(string filename, bool timeline, CancellationToken cancellation = default)
    {
        string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(timeline ? "time_local,target,download_bytes,upload_bytes" : "date,target,download_bytes,upload_bytes");
                Exception? writeError = null;
                // Exceptions must not cross an unmanaged SQLite callback boundary.
                Callback callback = (_, count, values, _) =>
                {
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        for (int i = 0; i < count; i++)
                        {
                            if (i > 0) writer.Write(',');
                            string value = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, i * IntPtr.Size)) ?? "";
                            if (timeline && i == 0) value = DateTimeOffset.FromUnixTimeSeconds(long.Parse(value, CultureInfo.InvariantCulture)).ToLocalTime().ToString("O");
                            writer.Write(CsvCell(value));
                        }
                        writer.WriteLine();
                        return 0;
                    }
                    catch (Exception error) { writeError = error; return 1; }
                };
                try
                {
                    Exec(timeline ? "SELECT stamp,'',download,upload FROM timeline_seconds UNION ALL SELECT s.stamp,n.target,s.download,s.upload FROM timeline_target_seconds s JOIN timeline_names n ON n.id=s.target_id ORDER BY 1,2;" :
                        "SELECT day,target,download,upload FROM daily ORDER BY day,target;", callback);
                }
                catch (IOException) when (writeError != null) { throw writeError; }
                GC.KeepAlive(callback);
                cancellation.ThrowIfCancellationRequested();
            }
            File.Move(temporary, filename, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string CsvCell(string value)
    {
        // Prevent spreadsheet formula execution, and preserve commas/quotes/newlines in target names.
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}