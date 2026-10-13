using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ProxyTrafficFloat;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool testing = args.Length > 1 && args[0] == "--ui-test";
        string instanceName = @"Local\ProxyTrafficFloat.User." + Environment.UserName + (testing ? ".UiTest" : ".Settings");
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Activate");
        using var mutex = new Mutex(false, instanceName);
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns) { activation.Set(); return; }

        var app = new MonitorApp(args) { Activation = activation };
        app.DispatcherUnhandledException += (_, e) =>
        {
            if (testing)
            {
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "settings-error.txt"), e.Exception.ToString());
                app.Shutdown(1);
            }
            else MessageBox.Show(Strings.T("设置未能完成：") + Strings.T(e.Exception.Message), "Traffic Lens");
            e.Handled = true;
        };
        try { app.Run(); }
        finally { mutex.ReleaseMutex(); }
    }
}

/// <summary>Settings host only. Sampling and counting belong to the native resident.</summary>
public sealed class MonitorApp : Application
{
    public EventWaitHandle? Activation { get; init; }
    public Settings Config { get; private set; } = new();
    public TrafficStore Store { get; private set; } = null!;
    public Dictionary<string, Amount> Today { get; private set; } = new();
    public string Status { get; private set; } = "正在读取本机监控状态…";
    public string Source { get; private set; } = "本机接口";
    public bool Connected { get; private set; }
    public bool IsSettingsHost => true;
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
        dataRoot = args.Length > 1 && args[0] == "--ui-test"
            ? Path.Combine(args[1], "test-session", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyTrafficFloat");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Config = Settings.Load(ConfigPath);
        Strings.UseLanguage(Config.Language);
        baseline = Settings.Load(ConfigPath);
        Store = new TrafficStore(Path.Combine(dataRoot, "traffic.sqlite"));
        Today = Store.Today();
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
        if (arguments.Length > 1 && arguments[0] == "--ui-test") _ = CapturePagesAsync(arguments[1]);
    }

    void ReadStatus()
    {
        // JSON is a local status snapshot. The settings process never samples Mihomo itself.
        string path = Path.Combine(dataRoot, "live.json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            Connected = root.GetProperty("connected").GetBoolean();
            Status = root.GetProperty("status").GetString() ?? "未连接";
            Source = root.GetProperty("source").GetString() ?? "本机接口";
            if (DateTimeOffset.UtcNow - root.GetProperty("at").GetDateTimeOffset() > TimeSpan.FromSeconds(30)) { Connected = false; Status = "常驻监控未运行"; }
        }
        catch (IOException) { Connected = false; Status = "常驻监控未运行"; }
        catch (JsonException) { Connected = false; Status = "正在读取监控状态…"; }
        DateTime time = File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue;
        if (time != configTime)
        {
            MergeWithDisk();
            baseline = Settings.Load(ConfigPath);
            configTime = time;
        }
        Today = Store.Today();
        Updated?.Invoke();
    }

    void MergeWithDisk()
    {
        var latest = Settings.Load(ConfigPath);
        // Preserve pending edits while accepting geometry written by the resident window.
        foreach (var property in typeof(Settings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == nameof(Settings.Secret)) continue;
            if (!Equals(property.GetValue(Config), property.GetValue(baseline)))
                property.SetValue(latest, property.GetValue(Config));
        }
        Config = latest;
    }

    public void Save()
    {
        MergeWithDisk();
        Config.Save(ConfigPath);
        baseline = Settings.Load(ConfigPath);
        configTime = File.GetLastWriteTimeUtc(ConfigPath);
    }
    public void Apply() => Updated?.Invoke();
    public void Recalculate() { }
    public void Reconnect() { Status = "设置已保存，常驻监控正在重新连接"; Updated?.Invoke(); }
    public void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => { settings = null; Shutdown(); };
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Show();
        settings.Activate();
    }
    public void ClearStatistics()
    {
        // One owner clears both the database and the resident's cached totals on the next tick.
        File.WriteAllText(Path.Combine(dataRoot, "clear.signal"), "clear");
        Store.Clear();
        Today.Clear();
        Updated?.Invoke();
    }
    public void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("ProxyTrafficFloat", "\"" + Path.Combine(AppContext.BaseDirectory, "ProxyTrafficFloat.exe") + "\"");
        else key.DeleteValue("ProxyTrafficFloat", false);
        Config.AutoStart = enabled;
    }

    async Task CapturePagesAsync(string output)
    {
        Directory.CreateDirectory(output);
        // Public screenshots use example data in the isolated test directory, never real browsing history.
        Store.Add(new[] { new Increment(DateTime.Today, "downloads.example", 186L * 1024 * 1024, 2048), new Increment(DateTime.Today, "uploads.example", 4096, 24L * 1024 * 1024) });
        Config.RecordTimeline = true;
        Save();
        long end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var buckets = new List<TimelineBucket>();
        for (int i = 1; i < 1800; i++)
        {
            if (i is >= 700 and <= 750) continue;
            long time = end-i;
            long d=2048+(i%17==0?24*1024:0)+(i is >= 88 and <= 91?11L*1024*1024:0)+(i is >= 310 and <= 313?3L*1024*1024:0);
            long u=512+(i%23==0?8192:0)+(i is >= 168 and <= 170?5L*1024*1024:0);
            long low=i%31==0?128*1024:4096;
            foreach (var item in new[] { ("",d+low,u,1.0), ("downloads.example",d,0L,0.0), ("uploads.example",0L,u,0.0), ("low.example",low,0L,0.0) })
            {
                var bucket = new TimelineBucket(time,item.Item1);
                bucket.Add(item.Item2,item.Item3,item.Item4,time*1000,(time+1)*1000);
                buckets.Add(bucket);
            }
        }
        Store.AddTimeline(buckets);
        File.WriteAllText(Path.Combine(dataRoot, "live.json"), JsonSerializer.Serialize(new { connected = true, status = "内核在线 · 本机命名管道", source = "命名管道", at = DateTimeOffset.UtcNow }));
        ReadStatus();
        try
        {
            await Task.Delay(600);
            await settings!.CheckToggleAnimationAsync(output);
            await settings.VerifyNavigationAsync(output);
            var face=new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Microsoft YaHei"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            if(!face.TryGetGlyphTypeface(out var glyphs) || !glyphs.CharacterToGlyphMap.ContainsKey('悬')) throw new InvalidOperationException("Chinese UI font resolved to a missing glyph.");
            File.WriteAllText(Path.Combine(output,"font-test.txt"),"PASS: Chinese glyphs resolve to "+glyphs.FontUri+"; UI family="+Ui.InterfaceFont.Source);
            settings.SelectPage(2);
            settings.ShowTimeline();
            await Task.Delay(240);
            settings.VerifyTimelineForTest(output);
            await settings.VerifyStatisticsForTest(output);
            foreach (string language in new[] { "zh-CN", "en" })
            {
                settings.SetLanguage(language);
                foreach (bool small in new[] { false, true })
                {
                    settings.Width = small ? 520 : 820;
                    settings.Height = small ? 360 : 650;
                    for (int i = 0; i < 4; i++)
                    {
                        settings.SelectPage(i);
                        if (i == 2) settings.SelectStatisticsView(0, false);
                        await Task.Delay(240);
                        SavePage($"settings-{language}{(small ? "-small" : "")}-{i}.png");
                        if (i == 2)
                        {
                            settings.ShowTimeline();
                            await Task.Delay(240);
                            SavePage($"timeline-speed-{language}{(small ? "-small" : "")}.png");
                            File.AppendAllText(Path.Combine(output,"chart-layout.txt"),$"{language}, small={small}: {settings.TimelineLayoutForTest}\n");
                            Config.TimelineMetric = 1;
                            settings.SelectPage(2);
                            await Task.Delay(240);
                            SavePage($"timeline-usage-{language}{(small ? "-small" : "")}.png");
                            Config.TimelineMetric = 0;
                        }
                    }
                }
                settings.ScrollCurrentToEnd();
                await Task.Delay(100);
                SavePage($"settings-{language}-about.png");
            }
            settings!.Width=820; settings.Height=650;
            foreach(string language in new[]{"zh-CN","en"})
            {
                settings.SetLanguage(language); settings.SelectPage(2); settings.ShowTimeline();
                foreach (string theme in new[] { "light", "dark" })
                {
                    settings.SetTheme(theme);
                    settings.SelectStatisticsView(0, false); await Task.Delay(240);
                    SavePage("usage-" + language + "-" + theme + ".png");
                    settings.ShowTimeline(); await Task.Delay(200);
                    settings.PreviewTimelineForTest(); await Task.Delay(240);
                    SavePage("timeline-interaction-"+language+"-"+theme+".png");
                }
            }
            settings!.SetTheme("dark"); settings.SetLanguage("zh-CN"); settings.SelectPage(2); settings.ShowTimeline();
            settings.PreviewLowTrafficForTest(false); await Task.Delay(250); SavePage("timeline-low-burst.png");
            settings.PreviewLowTrafficForTest(true); await Task.Delay(250); SavePage("timeline-low-detail.png");
            settings.SelectPage(0);
            settings.OpenAppearanceForTest(); await Task.Delay(220);
            if(settings.AppearancePreviewForTest is FrameworkElement appearance)
            {
                var preview=new RenderTargetBitmap((int)Math.Ceiling(appearance.ActualWidth+20),(int)Math.Ceiling(appearance.ActualHeight+20),96,96,PixelFormats.Pbgra32);
                preview.Render(appearance); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview));
                using var appearanceStream=File.Create(Path.Combine(output,"appearance-picker.png")); png.Save(appearanceStream);
            }
            settings.CloseAppearanceForTest();
            File.WriteAllText(Path.Combine(output, "settings-test.txt"), "PASS: Chinese/English, four pages at normal and compact sizes, instant language change, toggle animation, isolated example data\n");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "settings-test.txt"), "FAIL: " + error);
        }
        finally { settings!.Close(); }

        void SavePage(string filename)
        {
            settings!.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(settings.ActualWidth), (int)Math.Ceiling(settings.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(settings);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, filename));
            encoder.Save(stream);
        }
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
