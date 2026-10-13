using System;
using System.IO;
using System.Reflection;

namespace ProxyTrafficFloat;

// Two fixed-size files per process kind. No paths, URLs, secrets or raw response bodies.
public static class Diagnostics
{
    static readonly object Sync = new();
    static string last = "";
    static long lastAt;
    public static void Write(string root, string operation, Exception error)
    {
        string entry = operation + " " + error.GetType().Name + " 0x" + error.HResult.ToString("X8");
        lock (Sync)
        {
            long now = Environment.TickCount64;
            if (root + entry == last && now - lastAt < 60000)
                return;
            last = root + entry;
            lastAt = now;
            try
            {
                string folder = Path.Combine(root, "logs");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, (Assembly.GetEntryAssembly()?.GetName().Name ?? "TrafficLens") + ".log");
                if (File.Exists(file) && new FileInfo(file).Length >= 256 * 1024)
                    File.Move(file, file + ".old", true);
                File.AppendAllText(file, DateTimeOffset.Now.ToString("o") + " " + entry + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { } // Diagnostics must not disable monitoring.
        }
    }
}
