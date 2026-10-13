using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ProxyTrafficFloat;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if TRAFFIC_LENS_VERIFICATION
        bool testing = args.Length > 1 && args[0] == "--ui-test";
#else
        if (args.Length > 0 && args[0] == "--ui-test") return 1;
        bool testing = false;
#endif
        if (testing) Environment.ExitCode = 1;
        string instanceName = @"Local\ProxyTrafficFloat.User." + Environment.UserName + (testing ? ".UiTest" : ".Settings");
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Activate");
        using var mutex = new Mutex(false, instanceName);
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns) { if (testing) return 1; activation.Set(); return 0; }

        var app = new MonitorApp(args) { Activation = activation };
        app.DispatcherUnhandledException += (_, e) =>
        {
            if (testing)
            {
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "settings-error.txt"), e.Exception.ToString());
                app.Shutdown(1);
            }
            else
            {
                Diagnostics.Write(app.DataDirectory, "settings-unhandled", e.Exception);
                MessageBox.Show(Strings.T("设置窗口发生错误，请重新打开。"), "Traffic Lens");
                app.Shutdown(1);
            }
            Environment.ExitCode = 1;
            e.Handled = true;
        };
        try { return app.Run(); }
        finally { mutex.ReleaseMutex(); }
    }
}

/// <summary>Settings host only. Sampling and counting belong to the native resident.</summary>
public sealed partial class MonitorApp : Application
{
    public EventWaitHandle? Activation { get; init; }
    public Settings Config { get; private set; } = new();
    public TrafficStore Store { get; private set; } = null!;

    public string Status { get; private set; } = "正在读取本机监控状态…";
    public string Source { get; private set; } = "本机接口";
    public bool Connected { get; private set; }
    public NodeLatency? CurrentLatency { get; private set; }
    public string DataDirectory => dataRoot;
    public bool IsStopping { get; private set; }
    public event Action? Updated;

    readonly string[] arguments;
    readonly string dataRoot;
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    RegisteredWaitHandle? activationWait;
    Settings baseline = new();
    DateTime configTime;
    SettingsWindow? settings;
    string ConfigPath => Path.Combine(dataRoot, "settings.json");

    public MonitorApp(string[] args)
    {
        arguments = args;
        dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyTrafficFloat");
#if TRAFFIC_LENS_VERIFICATION
        if (args.Length > 1 && args[0] == "--ui-test")
            dataRoot = Path.Combine(args[1], "test-session", Guid.NewGuid().ToString("N"));
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Config = Settings.Load(ConfigPath);
        Strings.UseLanguage(Config.Language);
        baseline = Config.Copy();
        Store = new TrafficStore(Path.Combine(dataRoot, "traffic.sqlite"));

        OpenSettings();
        MainWindow = settings;
        refresh.Tick += (_, _) => ReadStatus();
        refresh.Start();
        if (Activation != null)
            activationWait = ThreadPool.RegisterWaitForSingleObject(Activation, (_, _) =>
            {
                if (!IsStopping) Dispatcher.BeginInvoke(new Action(() => { if (!IsStopping) OpenSettings(); }));
            }, null, Timeout.Infinite, false);
        ReadStatus();
#if TRAFFIC_LENS_VERIFICATION
        if (arguments.Length > 1 && arguments[0] == "--ui-test") _ = CapturePagesAsync(arguments[1]);
#endif
    }

    void ReadStatus()
    {
        // JSON is a local status snapshot. The settings process never samples Mihomo itself.
        string path = Path.Combine(dataRoot, "live.json");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            using var reader = new StreamReader(stream);
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("connected", out var online) || online.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String || !at.TryGetDateTimeOffset(out var timestamp))
                throw new JsonException("Invalid local status.");
            Connected = online.GetBoolean();
            CurrentLatency = root.TryGetProperty("latency", out var node) && node.ValueKind == JsonValueKind.Object ? JsonSerializer.Deserialize<NodeLatency>(node) : null;
            Status = ConnectionParser.GetString(root, "status");
            Source = ConnectionParser.GetString(root, "source");
            if (DateTimeOffset.UtcNow - timestamp > TimeSpan.FromSeconds(30)) { Connected = false; Status = "常驻监控未运行"; }

        }
        catch (FileNotFoundException) { Connected = false; Status = "常驻监控未运行"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Connected = false;
            Status = "无法读取本机状态，请查看日志。";
            Diagnostics.Write(dataRoot, "settings-status", error);
        }
        try
        {
            DateTime time = File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue;
            if (time != configTime)
            {
                var latest = Settings.Load(ConfigPath);
                var saved = latest.Copy();
                MergeWithDisk(latest);
                Config = latest;
                baseline = saved;
                configTime = saved.ModifiedUtc;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = "设置读取失败，请查看日志。";
            Diagnostics.Write(dataRoot, "settings-read", error);
        }
        Updated?.Invoke();
    }

    static readonly PropertyInfo[] SavedProperties = typeof(Settings).GetProperties()
        .Where(p => p.SetMethod?.IsPublic == true && p.Name != nameof(Settings.Secret)).ToArray();

    void MergeWithDisk(Settings latest)
    {
        // Preserve pending edits while accepting geometry written by the resident window.
        foreach (var property in SavedProperties)
        {

            if (!Equals(property.GetValue(Config), property.GetValue(baseline)))
                property.SetValue(latest, property.GetValue(Config));
        }
    }

    public void Save()
    {
        Config = Settings.Update(ConfigPath, MergeWithDisk);
        baseline = Config.Copy();
        configTime = Config.ModifiedUtc;
    }
    public void Apply() => Updated?.Invoke();

    public void Reconnect()
    {
        File.WriteAllText(Path.Combine(dataRoot, "reconnect.signal"), "");
        Status = "设置已保存，常驻监控正在重新连接";
        Updated?.Invoke();
    }
    public void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => { settings = null; Shutdown(Environment.ExitCode); };
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Show();
        settings.Activate();
    }
    public void ClearStatistics()
    {
        // One owner clears both the database and the resident's cached totals on the next tick.
        Store.Clear();
        File.WriteAllText(Path.Combine(dataRoot, "clear.signal"), "clear");

        Updated?.Invoke();
    }
    public void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("ProxyTrafficFloat", "\"" + Path.Combine(AppContext.BaseDirectory, "ProxyTrafficFloat.exe") + "\"");
        else key.DeleteValue("ProxyTrafficFloat", false);
        Config.AutoStart = enabled;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsStopping = true;
        activationWait?.Unregister(null);
        refresh.Stop();
        Store?.Dispose();
        base.OnExit(e);
    }
}
