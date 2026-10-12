using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProxyTrafficFloat;

namespace ProxyTrafficFloat.Native;

public record NativeFrame(TrafficView View, bool Connected, string Status, string Source, long TodayDownload, long TodayUpload, long DownloadTargetTotal, long UploadTargetTotal)
{
    public static NativeFrame Empty => new(new(0, 0, null, null, 0, 0, 0), false, Strings.T("正在连接…"), "", 0, 0, 0, 0);
}
public record Screen(string Name, Win.Rect Work, double Scale);

public sealed class NativeMonitor : IDisposable
{
    const uint UpdateMessage = 0x8001, ConfigMessage = 0x8002, TrayMessage = 0x8003, ActivateMessage = 0x8004;
    readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyTrafficFloat");
    readonly string[] args;
    readonly EventWaitHandle activate;
    RegisteredWaitHandle? activationWait;
    readonly CancellationTokenSource stop = new(); readonly Stopwatch watch = Stopwatch.StartNew();
    readonly CompactModeController compact = new(); readonly Canvas canvas = new();
    readonly Win.WindowProc procedure;
    Settings config; NativeFrame frame = NativeFrame.Empty;
    NativeTip? tip;
    IntPtr hwnd, unlockWindow; List<Screen>? dragScreens;
    Task? worker; Process? settingsProcess;
    bool mini, restored, dragging, closing, geometryBusy;
    bool dragArmed, dragWasMini;
    Win.Point dragOrigin;
    Win.Rect dragBounds;
    int testPhase;
    int hoverButton = -1;
    IntPtr trackedMouse;
    double settingsStarted;
    int pixelWidth, pixelHeight, success, failures; double drawScale = 1;
    Win.IconData icon; string lastTip = "";
    readonly string ConfigPath, LivePath, clearPath;
    public NativeMonitor(string[] args, EventWaitHandle activate)
    {
        this.args = args;
        this.activate = activate;
        if (args.Length > 1 && args[0] is "--native-test" or "--timeline-soak")
        {
            string location = args[0] == "--native-test" ? args[1] : Path.GetDirectoryName(args[1])!;
            root = Path.Combine(location, "test-session", Guid.NewGuid().ToString("N"));
        }
        ConfigPath = Path.Combine(root, "settings.json");
        LivePath = Path.Combine(root, "live.json");
        clearPath = Path.Combine(root, "clear.signal");
        Directory.CreateDirectory(root);
        config = Settings.Load(ConfigPath);
        AppTheme.Apply(config.ColorMode);
        Strings.UseLanguage(config.Language);
        frame = NativeFrame.Empty;
        if (args.Length > 0 && args[0] is "--native-test" or "--timeline-soak")
        {
            config.Visible = false;
            config.RecordTimeline = true;
        }
        procedure = WndProc;
    }
    public int Run()
    {
        var instance = Win.GetModuleHandle(null);
        var cls = new Win.WindowClass { Size = (uint)Marshal.SizeOf<Win.WindowClass>(), Name = "ProxyTrafficFloat.NativeWindow", Proc = procedure, Instance = instance, Cursor = Win.LoadCursor(IntPtr.Zero, new IntPtr(32512)) };
        if (Win.RegisterClassEx(ref cls) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        hwnd = Win.CreateWindowEx(0x08080088, cls.Name, Strings.T("代理流量悬浮窗"), 0x80000000, 30, 100, 304, 162, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new IOException(Strings.T("创建悬浮窗失败"));
        icon = new Win.IconData { Size = (uint)Marshal.SizeOf<Win.IconData>(), Hwnd = hwnd, Id = 1, Flags = 7, Callback = TrayMessage, Icon = TrayIcon.Create(), Tip = Strings.T("代理流量 · 正在连接"), Info = "", Title = "" };
        Win.Shell_NotifyIcon(0, ref icon);
        tip = new NativeTip(hwnd);
        ApplyGeometry();
        ApplyVisibility();
        Win.SetTimer(hwnd, new IntPtr(1), 250, IntPtr.Zero);
        activationWait = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Win.PostMessage(hwnd, ActivateMessage, IntPtr.Zero, IntPtr.Zero), null, Timeout.Infinite, false);
        worker = Task.Run(MonitorLoopAsync);
        if (args.Length > 1 && args[0] is "--soak" or "--timeline-soak")
            _ = Task.Run(() => SoakAsync(args[1], args.Length > 2 && int.TryParse(args[2], out var duration) ? duration : 3600));
        while (Win.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            Win.TranslateMessage(ref message);
            Win.DispatchMessage(ref message);
        }
        stop.Cancel();
        try
        {
            worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        return 0;
    }
    async Task MonitorLoopAsync()
    {
        using var client = new MihomoClient();
        var engine = new TrafficEngine();
        using var store = new TrafficStore(Path.Combine(root, "traffic.sqlite"));
        var today = store.Today();
        var timeline = new TimelineRecorder(store);
        DateTime day = DateTime.Today, configTime = File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue;
        string endpoint = config.Endpoint, secret = config.ProtectedSecret;
        LiveStatus? published = null;
        double publishedAt = -15, prunedAt = -60;
        using var interval = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    var time = File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue;
                    if (time != configTime)
                    {
                        var fresh = Settings.Load(ConfigPath);
                        bool modeChanged = config.MiniMode != fresh.MiniMode;
                        Volatile.Write(ref config, fresh);
                        Strings.UseLanguage(fresh.Language);
                        configTime = time;
                        Win.PostMessage(hwnd, ConfigMessage, modeChanged ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
                    }
                    var cfg = Volatile.Read(ref config);
                    if (cfg.Endpoint != endpoint || cfg.ProtectedSecret != secret)
                    {
                        client.Dispose();
                        engine.Reset();
                        endpoint = cfg.Endpoint;
                        secret = cfg.ProtectedSecret;
                    }
                    if (day != DateTime.Today)
                    {
                        day = DateTime.Today;
                        store.Prune(day);
                        store.PruneTimeline(DateTimeOffset.UtcNow);
                        today = store.Today();
                    }
                    // Consume clear requests even while the API is unavailable, before pending history can flush.
                    if (File.Exists(clearPath))
                    {
                        timeline.Clear();
                        store.Clear();
                        today.Clear();
                        File.Delete(clearPath);
                    }
                    if (watch.Elapsed.TotalSeconds - prunedAt >= 60)
                    {
                        store.PruneTimeline(DateTimeOffset.UtcNow);
                        prunedAt = watch.Elapsed.TotalSeconds;
                    }
                    var snapshot = await client.ReadAsync(cfg, stop.Token);
                    double sampleTime = watch.Elapsed.TotalSeconds;
                    var sampledAt = DateTimeOffset.Now;
                    var increments = engine.Sample(snapshot, sampleTime, sampledAt.DateTime);
                    string status = Strings.T("内核在线 · ") + Strings.T(client.Source);
                    try
                    {
                        store.Add(increments);
                        foreach (var increment in increments)
                        {
                            if (increment.Day.Date != day)
                                continue;
                            today.TryGetValue(increment.Target, out var old);
                            today[increment.Target] = new((old?.Download ?? 0) + increment.Download, (old?.Upload ?? 0) + increment.Upload);
                        }
                        timeline.Observe(increments, sampleTime, sampledAt, cfg.RecordTimeline);
                    }
                    catch { status = Strings.T("实时监控正常 · 统计保存失败"); }
                    var view = engine.View(cfg.SingleConnection);
                    today.TryGetValue(view.DownloadTop?.Target ?? "", out var down);
                    today.TryGetValue(view.UploadTop?.Target ?? "", out var up);
                    Volatile.Write(ref frame, new(view, true, status, client.Source, today.Values.Sum(a => a.Download), today.Values.Sum(a => a.Upload), down?.Download ?? 0, up?.Upload ?? 0));
                    Interlocked.Increment(ref success);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch
                {
                    engine.Reset();
                    try { timeline.Break(); } catch (IOException) { }
                    Volatile.Write(ref frame, NativeFrame.Empty with { Status = Strings.T("连接中断 · 自动重连") });
                    Interlocked.Increment(ref failures);
                }
                var latest = Volatile.Read(ref frame);
                // Publish changes immediately; an idle heartbeat every 15 seconds avoids repeated disk writes.
                if (published == null || published.connected != latest.Connected || published.status != latest.Status || published.source != latest.Source || watch.Elapsed.TotalSeconds - publishedAt >= 15)
                {
                    try
                    {
                        published = new LiveStatus(latest.Connected, latest.Status, latest.Source, DateTimeOffset.UtcNow);
                        var json = JsonSerializer.Serialize(published, MonitorJson.Default.LiveStatus);
                        var tmp = LivePath + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, LivePath, true);
                        publishedAt = watch.Elapsed.TotalSeconds;
                    }
                    catch (IOException) { }
                }
                Win.PostMessage(hwnd, UpdateMessage, IntPtr.Zero, IntPtr.Zero);
            } while (await interval.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { try { timeline.Flush(); } catch (IOException) { } }
    }
    IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        try
        {
            if (window == unlockWindow && unlockWindow != IntPtr.Zero)
                return UnlockProc(window, message, w, l);
            switch (message)
            {
                case 0x0014:
                    return new IntPtr(1);
                case 0x0020:
                    if (hoverButton >= 0 && (l.ToInt64() & 0xffff) == 1)
                    {
                        Win.SetCursor(Win.LoadCursor(IntPtr.Zero, new IntPtr(32649)));
                        return new IntPtr(1);
                    }
                    break;
                case 0x000F:
                    var dc = Win.BeginPaint(window, out var paint);
                    try
                    {
                        canvas.Render(Volatile.Read(ref frame), config, mini, hoverButton);
                        canvas.Paint(dc);
                    }
                    finally { Win.EndPaint(window, ref paint); }
                    return IntPtr.Zero;
                case UpdateMessage:
                    RefreshMode();
                    UpdateTip();
                    UpdateDetails();
                    Win.InvalidateRect(window, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case ConfigMessage:
                    RefreshLanguage();
                    if (w != IntPtr.Zero)
                        restored = false;
                    ApplyGeometry();
                    ApplyVisibility();
                    return IntPtr.Zero;
                case ActivateMessage:
                    Change(c => c.Visible = true);
                    return IntPtr.Zero;
                case 0x0113:
                    RefreshMode();
                    if (args.Length > 1 && args[0] == "--native-test")
                        NativeTest();
                    return IntPtr.Zero;
                case 0x02E0:
                case 0x007E:
                case 0x001A:
                    AppTheme.Apply(config.ColorMode, true);
                    Win.InvalidateRect(hwnd, IntPtr.Zero, false);
                    ApplyGeometry();
                    return IntPtr.Zero;
                case 0x0216:
                    if (config.SnapToEdges && dragging)
                    {
                        var moving = Marshal.PtrToStructure<Win.Rect>(l);
                        var available = dragScreens ?? Screens();
                        var monitor = available.OrderByDescending(s => Math.Max(0, Math.Min(s.Work.Right, moving.Right) - Math.Max(s.Work.Left, moving.Left)) * Math.Max(0, Math.Min(s.Work.Bottom, moving.Bottom) - Math.Max(s.Work.Top, moving.Top))).First();
                        var snapped = ScreenGeometry.Snap(moving.Left, moving.Top, monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top, moving.Right - moving.Left, moving.Bottom - moving.Top, 16 * monitor.Scale);
                        int mw = moving.Right - moving.Left, mh = moving.Bottom - moving.Top;
                        moving.Left = (int)Math.Round(snapped.X);
                        moving.Top = (int)Math.Round(snapped.Y);
                        moving.Right = moving.Left + mw;
                        moving.Bottom = moving.Top + mh;
                        Marshal.StructureToPtr(moving, l, false);
                        return new IntPtr(1);
                    }
                    break;
                case 0x0201:
                    int x = (short)(l.ToInt64() & 0xffff), y = (short)((l.ToInt64() >> 16) & 0xffff);
                    int lx = (int)(x / drawScale), ly = (int)(y / drawScale), width = (int)Math.Round(pixelWidth / drawScale);
                    if (IsLockPoint(lx, ly, width))
                    {
                        Change(c => c.Locked = !c.Locked);
                        return IntPtr.Zero;
                    }
                    if (config.Locked)
                        return IntPtr.Zero;
                    if (!mini && ly < 27 && lx >= width - 92)
                    {
                        if (lx < width - 68)
                            OpenSettings();
                        else if (lx < width - 46)
                            Change(c => c.MiniMode = !c.MiniMode);
                        else if (lx >= width - 24)
                            Change(c => c.Visible = false);
                        return IntPtr.Zero;
                    }
                    Win.GetCursorPos(out var cursor);
                    BeginPointerDrag(cursor);
                    return IntPtr.Zero;
                case 0x0200:
                    if (dragArmed)
                    {
                        Win.GetCursorPos(out var pointer);
                        MovePointerDrag(pointer);
                        return IntPtr.Zero;
                    }
                    TrackButtons(window, l, false);
                    return IntPtr.Zero;
                case 0x02A3: // WM_MOUSELEAVE: repaint only when the highlighted button changes.
                    trackedMouse = IntPtr.Zero;
                    SetHoveredButton(-1);
                    return IntPtr.Zero;
                case 0x0202:
                    if (dragArmed)
                    {
                        EndPointerDrag(true);
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0215:
                case 0x001F:
                    if (dragArmed)
                        EndPointerDrag(false);
                    break;
                case 0x0205:
                    if (!config.Locked)
                        ShowMenu();
                    return IntPtr.Zero;
                case TrayMessage:
                    if (l.ToInt32() == 0x0205)
                        ShowMenu();
                    else if (l.ToInt32() == 0x0203)
                        OpenSettings();
                    return IntPtr.Zero;
                case 0x0010:
                    Close("window-close");
                    return IntPtr.Zero;
                case 0x0002:
                    closing = true;
                    stop.Cancel();
                    Win.KillTimer(window, new IntPtr(1));
                    Win.Shell_NotifyIcon(2, ref icon);
                    Win.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex) { if (args.Length > 1 && args[0] == "--native-test") { File.AppendAllText(Path.Combine(args[1], "native-test.txt"), "FAIL: " + ex + "\n"); Close("test-failure"); } else Win.MessageBox(window, Strings.T(ex.Message), Strings.T("代理流量"), 0x10); }
        return Win.DefWindowProc(window, message, w, l);
    }
    void RefreshMode()
    {
        if (dragArmed || dragging)
            return;
        var current = Volatile.Read(ref frame);
        var cfg = config;
        bool expanded = compact.Update(cfg.MiniMode, cfg.AutoExpand, current.Connected, current.View.DownloadTop?.DownloadSpeed ?? 0, current.View.UploadTop?.UploadSpeed ?? 0, cfg.DownloadThresholdKB * 1024, cfg.UploadThresholdKB * 1024, cfg.TriggerSeconds, cfg.QuietSeconds, watch.Elapsed.TotalSeconds);
        bool next = cfg.MiniMode && !expanded;
        if (next != mini)
        {
            mini = next;
            ApplyGeometry();
            Win.InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }
    void BeginPointerDrag(Win.Point pointer)
    {
        if (config.Locked)
            return;
        SetHoveredButton(-1);
        dragOrigin = pointer;
        Win.GetWindowRect(hwnd, out dragBounds);
        dragWasMini = mini;
        dragScreens = Screens();
        dragArmed = true;
        dragging = false;
        tip?.Enable(false);
        Win.SetCapture(hwnd);
    }
    void MovePointerDrag(Win.Point pointer)
    {
        if (!dragArmed || config.Locked)
            return;
        int dx = pointer.X - dragOrigin.X, dy = pointer.Y - dragOrigin.Y;
        if (!dragging && Math.Abs(dx) + Math.Abs(dy) <= 3 * drawScale)
            return;
        dragging = true;
        int x = dragBounds.Left + dx, y = dragBounds.Top + dy;
        int width = dragBounds.Right - dragBounds.Left, height = dragBounds.Bottom - dragBounds.Top;
        if (config.SnapToEdges)
        {
            var screens = dragScreens ?? Screens();
            var screen = screens.OrderByDescending(s => Math.Max(0, Math.Min(s.Work.Right, x + width) - Math.Max(s.Work.Left, x)) * Math.Max(0, Math.Min(s.Work.Bottom, y + height) - Math.Max(s.Work.Top, y))).First();
            var pos = ScreenGeometry.Snap(x, y, screen.Work.Left, screen.Work.Top, screen.Work.Right - screen.Work.Left, screen.Work.Bottom - screen.Work.Top, width, height, 16 * screen.Scale);
            x = (int)Math.Round(pos.X);
            y = (int)Math.Round(pos.Y);
        }
        Win.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0015);
    }
    void EndPointerDrag(bool allowClick)
    {
        bool moved = dragging, expand = allowClick && !moved && dragWasMini;
        dragArmed = false;
        dragging = false;
        dragScreens = null;
        if (Win.GetCapture() == hwnd)
            Win.ReleaseCapture();
        tip?.Enable(!config.Locked);
        if (moved)
        {
            RememberPosition();
            ApplyGeometry();
        }
        else if (expand)
        {
            compact.ManualExpand(watch.Elapsed.TotalSeconds);
            RefreshMode();
        }
    }
    List<Screen> Screens()
    {
        var list = new List<Screen>();
        Win.MonitorProc callback = (monitor, dc, rect, arg) => { var info = new Win.MonitorInfo { Size = Marshal.SizeOf<Win.MonitorInfo>() }; if (Win.GetMonitorInfo(monitor, ref info)) { double scale = 1; try { if (Win.GetDpiForMonitor(monitor, 0, out var dpi, out var other) == 0) scale = dpi / 96.0; } catch { } list.Add(new(info.Name, info.Work, scale)); } return true; };
        Win.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        if (list.Count == 0)
            list.Add(new("primary", new Win.Rect { Right = 1920, Bottom = 1080 }, 1));
        return list;
    }
    Screen CurrentScreen(bool preferred)
    {
        var screens = Screens();
        if (preferred)
        {
            var found = screens.FirstOrDefault(s => s.Name == config.MonitorName);
            if (found != null)
                return found;
        }
        Win.GetWindowRect(hwnd, out var r);
        return screens.OrderByDescending(s => Math.Max(0, Math.Min(s.Work.Right, r.Right) - Math.Max(s.Work.Left, r.Left)) * Math.Max(0, Math.Min(s.Work.Bottom, r.Bottom) - Math.Max(s.Work.Top, r.Top))).First();
    }
    void ApplyGeometry()
    {
        if (hwnd == IntPtr.Zero || geometryBusy || dragArmed || dragging || closing)
            return;
        geometryBusy = true;
        try
        {
            hoverButton = -1;
            var cfg = config;
            var current = Volatile.Read(ref frame);
            bool next = cfg.MiniMode && !compact.IsExpanded;
            mini = next;
            var screen = CurrentScreen(!restored || cfg.MiniMode);
            if (cfg.MiniMode && cfg.MiniHasAnchor)
            {
                var preferred = Screens().FirstOrDefault(s => s.Name == cfg.MiniMonitorName);
                if (preferred != null)
                    screen = preferred;
            }
            double ww = screen.Work.Right - screen.Work.Left, hh = screen.Work.Bottom - screen.Work.Top;
            int logicalWidth = mini ? 220 : 304, logicalHeight = mini ? 58 : 28 + (cfg.ShowSpeed ? 22 : 0) + (cfg.ShowDownload || cfg.ShowUpload ? (cfg.ShowTargetTotal ? 91 : 76) : 0) + (cfg.ShowTotal ? 19 : 0) + 23;
            drawScale = screen.Scale * Math.Min(cfg.UiScale, Math.Min((ww - 16 * screen.Scale) / (logicalWidth * screen.Scale), (hh - 16 * screen.Scale) / (logicalHeight * screen.Scale)));
            drawScale = Math.Max(0.5, drawScale);
            int width = Math.Max(1, (int)Math.Round(logicalWidth * drawScale)), height = Math.Max(1, (int)Math.Round(logicalHeight * drawScale));
            Win.GetWindowRect(hwnd, out var r);
            (double X, double Y) position;
            if (cfg.MiniMode && cfg.MiniHasAnchor && !cfg.MiniDockCorner)
                position = ScreenGeometry.Clamp(screen.Work.Left + cfg.MiniAnchorX * (ww - width), screen.Work.Top + cfg.MiniAnchorY * (hh - height), screen.Work.Left, screen.Work.Top, ww, hh, width, height);
            else if (cfg.MiniMode)
                position = ScreenGeometry.Corner(screen.Work.Left, screen.Work.Top, ww, hh, width, height, cfg.Corner, 8 * screen.Scale);
            else if (!restored && cfg.HasAnchor)
                position = ScreenGeometry.Clamp(screen.Work.Left + cfg.AnchorX * (ww - width), screen.Work.Top + cfg.AnchorY * (hh - height), screen.Work.Left, screen.Work.Top, ww, hh, width, height);
            else if (!restored)
                position = ScreenGeometry.Clamp(cfg.Left * screen.Scale, cfg.Top * screen.Scale, screen.Work.Left, screen.Work.Top, ww, hh, width, height);
            else
                position = ScreenGeometry.Clamp(r.Left, r.Top, screen.Work.Left, screen.Work.Top, ww, hh, width, height);
            Win.SetWindowPos(hwnd, config.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2), (int)Math.Round(position.X), (int)Math.Round(position.Y), width, height, 0x0010);
            restored = true;
            if (pixelWidth != width || pixelHeight != height)
            {
                pixelWidth = width;
                pixelHeight = height;
                var region = Win.CreateRoundRectRgn(0, 0, width + 1, height + 1, (int)(18 * drawScale), (int)(18 * drawScale));
                if (!Win.SetWindowRgn(hwnd, region, true))
                    Win.DeleteObject(region);
            }
            canvas.Resize(width, height, drawScale);
            Win.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(cfg.Opacity * 255), 2);
            int flags = Win.GetStyle(hwnd, -20);
            Win.SetStyle(hwnd, -20, (cfg.Locked ? cfg.LockClickThrough : cfg.ClickThrough) ? flags | 0x20 : flags & ~0x20);
            SyncLockWindow();
            tip?.Enable(!cfg.Locked);
            Win.InvalidateRect(hwnd, IntPtr.Zero, false);
        }
        finally { geometryBusy = false; }
    }
    void ApplyVisibility()
    {
        Win.ShowWindow(hwnd, config.Visible ? 4 : 0);
        if (unlockWindow != IntPtr.Zero)
            Win.ShowWindow(unlockWindow, config.Visible ? 4 : 0);
        if (config.Visible)
            Win.SetWindowPos(hwnd, config.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    void RefreshLanguage()
    {
        AppTheme.Apply(config.ColorMode);
        Strings.UseLanguage(config.Language);
        Win.SetWindowText(hwnd, Strings.T("代理流量悬浮窗"));
        if (unlockWindow != IntPtr.Zero) Win.SetWindowText(unlockWindow, Strings.T("解锁代理流量"));
        lastTip = "";
        UpdateTip();
        UpdateDetails();
    }
    void Change(Action<Settings> change)
    {
        var fresh = Settings.Load(ConfigPath);
        bool wasMini = fresh.MiniMode;
        change(fresh);
        fresh.Save(ConfigPath);
        config = fresh;
        RefreshLanguage();
        if (wasMini != fresh.MiniMode)
            restored = false;
        RefreshMode();
        ApplyGeometry();
        ApplyVisibility();
    }
    void RememberPosition()
    {
        var screen = CurrentScreen(false);
        Win.GetWindowRect(hwnd, out var r);
        Change(c => { if (c.MiniMode) { c.MiniDockCorner = false; c.MiniHasAnchor = true; c.MiniMonitorName = screen.Name; c.MiniAnchorX = Math.Clamp((double)(r.Left - screen.Work.Left) / Math.Max(1, screen.Work.Right - screen.Work.Left - (r.Right - r.Left)), 0, 1); c.MiniAnchorY = Math.Clamp((double)(r.Top - screen.Work.Top) / Math.Max(1, screen.Work.Bottom - screen.Work.Top - (r.Bottom - r.Top)), 0, 1); return; } c.MonitorName = screen.Name; c.HasAnchor = true; c.AnchorX = Math.Clamp((double)(r.Left - screen.Work.Left) / Math.Max(1, screen.Work.Right - screen.Work.Left - (r.Right - r.Left)), 0, 1); c.AnchorY = Math.Clamp((double)(r.Top - screen.Work.Top) / Math.Max(1, screen.Work.Bottom - screen.Work.Top - (r.Bottom - r.Top)), 0, 1); c.Left = r.Left / screen.Scale; c.Top = r.Top / screen.Scale; });
    }
    bool IsLockPoint(int x, int y, int width) => mini ? x >= width - 25 && y >= 0 && y < 58 : x >= width - 46 && x < width - 24 && y >= 0 && y < 27;
    void TrackButtons(IntPtr window, IntPtr position, bool unlock)
    {
        if (trackedMouse != window)
        {
            var tracking = new Win.MouseTracking { Size = (uint)Marshal.SizeOf<Win.MouseTracking>(), Flags = 2, Window = window };
            if (Win.TrackMouseEvent(ref tracking)) trackedMouse = window;
        }
        int x = (int)((short)(position.ToInt64() & 0xffff) / drawScale);
        int y = (int)((short)((position.ToInt64() >> 16) & 0xffff) / drawScale);
        int width = (int)Math.Round(pixelWidth / drawScale);
        int button = -1;
        if (unlock || IsLockPoint(x, y, width)) button = 2;
        else if (!config.Locked && !mini && y >= 0 && y < 27 && x >= width - 92)
            button = x < width - 68 ? 0 : x < width - 46 ? 1 : x >= width - 24 ? 3 : -1;
        SetHoveredButton(button);
    }
    void SetHoveredButton(int button)
    {
        if (hoverButton == button) return;
        hoverButton = button;
        Win.InvalidateRect(hwnd, IntPtr.Zero, false);
        if (unlockWindow != IntPtr.Zero) Win.InvalidateRect(unlockWindow, IntPtr.Zero, false);
    }
    Win.Rect LockRect()
    {
        int width = (int)Math.Round(pixelWidth / drawScale);
        int x = mini ? width - 24 : width - 46, y = mini ? 5 : 4, w = 22, h = mini ? 48 : 22;
        return new Win.Rect { Left = (int)Math.Round(x * drawScale), Top = (int)Math.Round(y * drawScale), Right = (int)Math.Round((x + w) * drawScale), Bottom = (int)Math.Round((y + h) * drawScale) };
    }
    IntPtr UnlockProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        if (message == 0x0200)
        {
            TrackButtons(window, l, true);
            return IntPtr.Zero;
        }
        if (message == 0x0020 && (l.ToInt64() & 0xffff) == 1)
        {
            Win.SetCursor(Win.LoadCursor(IntPtr.Zero, new IntPtr(32649)));
            return new IntPtr(1);
        }
        if (message == 0x02A3)
        {
            trackedMouse = IntPtr.Zero;
            SetHoveredButton(-1);
            return IntPtr.Zero;
        }
        if (message == 0x0201)
        {
            Change(c => c.Locked = false);
            return IntPtr.Zero;
        }
        if (message == 0x0014)
            return new IntPtr(1);
        if (message == 0x000F)
        {
            var dc = Win.BeginPaint(window, out var paint);
            try
            {
                var r = LockRect();
                canvas.Render(Volatile.Read(ref frame), config, mini, hoverButton);
                canvas.PaintPart(dc, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            finally { Win.EndPaint(window, ref paint); }
            return IntPtr.Zero;
        }
        return Win.DefWindowProc(window, message, w, l);
    }
    void SyncLockWindow()
    {
        if (config.Locked && config.LockClickThrough)
        {
            if (unlockWindow == IntPtr.Zero)
                unlockWindow = Win.CreateWindowEx(0x08080088, "ProxyTrafficFloat.NativeWindow", Strings.T("解锁代理流量"), 0x80000000, 0, 0, 1, 1, hwnd, IntPtr.Zero, Win.GetModuleHandle(null), IntPtr.Zero);
            var local = LockRect();
            Win.GetWindowRect(hwnd, out var outer);
            Win.SetLayeredWindowAttributes(unlockWindow, 0, (byte)Math.Round(config.Opacity * 255), 2);
            Win.SetWindowPos(unlockWindow, config.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2), outer.Left + local.Left, outer.Top + local.Top, local.Right - local.Left, local.Bottom - local.Top, 0x0010);
            Win.ShowWindow(unlockWindow, config.Visible ? 4 : 0);
            Win.InvalidateRect(unlockWindow, IntPtr.Zero, false);
        }
        else if (unlockWindow != IntPtr.Zero)
        {
            Win.DestroyWindow(unlockWindow);
            unlockWindow = IntPtr.Zero;
        }
    }
    void UpdateDetails()
    {
        var latest = Volatile.Read(ref frame);
        string Target(ProxyTrafficFloat.RankedTarget? item, bool down) => item == null ? Strings.T("暂无流量") : $"{item.Target}\n{Strings.T(item.Process)} · {Strings.T(item.Node)}\n{Display.Speed(down ? item.DownloadSpeed : item.UploadSpeed)}";
        tip?.Update(Strings.T("下载最快") + "\n" + Target(latest.View.DownloadTop, true) + "\n\n" +
            Strings.T("上传最快") + "\n" + Target(latest.View.UploadTop, false) + "\n\n" + latest.Status + "\n" +
            Strings.T("顶部按钮：设置 · 迷你模式 · 锁定 · 隐藏"));
    }
    void UpdateTip()
    {
        var latest = Volatile.Read(ref frame);
        string text = latest.Connected ? Strings.Format("代理流量 ↓ {0} ↑ {1}", Display.Speed(latest.View.DownloadSpeed), Display.Speed(latest.View.UploadSpeed)) : Strings.T("代理流量 · 正在重连");
        if (text == lastTip)
            return;
        lastTip = text;
        icon.Tip = text;
        icon.Flags = 4;
        Win.Shell_NotifyIcon(1, ref icon);
    }
    void ShowMenu()
    {
        var menu = Win.CreatePopupMenu();
        try
        {
            Win.AppendMenu(menu, 0, new UIntPtr(101), config.Visible ? Strings.T("隐藏悬浮窗") : Strings.T("显示悬浮窗"));
            Win.AppendMenu(menu, 0, new UIntPtr(102), Strings.T("设置与流量记录"));
            Win.AppendMenu(menu, 0, new UIntPtr(103), config.MiniMode ? Strings.T("关闭角落迷你模式") : Strings.T("开启角落迷你模式"));
            Win.AppendMenu(menu, 0, new UIntPtr(106), Strings.T("立即收起到角落"));
            Win.AppendMenu(menu, 0, new UIntPtr(104), Strings.T("解除锁定与鼠标穿透"));
            Win.AppendMenu(menu, 0x800, UIntPtr.Zero, "");
            Win.AppendMenu(menu, 0, new UIntPtr(105), Strings.T("退出"));
            Win.GetCursorPos(out var point);
            Win.SetForegroundWindow(hwnd);
            uint action = Win.TrackPopupMenu(menu, 0x182, point.X, point.Y, 0, hwnd, IntPtr.Zero);
            switch (action)
            {
                case 101:
                    Change(c => c.Visible = !c.Visible);
                    break;
                case 102:
                    OpenSettings();
                    break;
                case 103:
                    Change(c => c.MiniMode = !c.MiniMode);
                    break;
                case 104:
                    Change(c => { c.Locked = false; c.ClickThrough = false; c.Visible = true; });
                    break;
                case 105:
                    Close("tray-exit");
                    break;
                case 106:
                    compact.Collapse(watch.Elapsed.TotalSeconds);
                    Change(c => { c.MiniMode = true; c.Visible = true; });
                    break;
            }
        }
        finally { Win.DestroyMenu(menu); }
    }
    void OpenSettings(bool testing = false)
    {
        if (settingsProcess != null && !settingsProcess.HasExited)
        {
            var handle = settingsProcess.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                Win.ShowWindow(handle, 9);
                Win.SetForegroundWindow(handle);
            }
            return;
        }
        settingsProcess?.Dispose();
        var executable = Path.Combine(AppContext.BaseDirectory, "ProxyTrafficSettings.exe");
        if (!File.Exists(executable))
            throw new IOException(Strings.T("找不到独立设置程序，请保留 app 文件夹中的全部文件。"));
        settingsProcess = Process.Start(new ProcessStartInfo(executable, testing ? "--ui-test \"" + args[1] + "\"" : "--settings-host") { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        settingsStarted = watch.Elapsed.TotalSeconds;
    }
    async Task SoakAsync(string output, int duration)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var writer = new StreamWriter(output) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakStart("start", DateTimeOffset.Now, duration, "native-gdi"), MonitorJson.Default.SoakStart));
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < duration && !stop.IsCancellationRequested)
            {
                await Task.Delay(Math.Min(30000, Math.Max(1, (int)((duration - clock.Elapsed.TotalSeconds) * 1000))), stop.Token);
                using var process = Process.GetCurrentProcess();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakSample("sample", clock.Elapsed.TotalSeconds, success, failures, process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false), process.HandleCount, Win.GetGuiResources(process.Handle, 0), Win.GetGuiResources(process.Handle, 1)), MonitorJson.Default.SoakSample));
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakResult("complete", clock.Elapsed.TotalSeconds, success, failures), MonitorJson.Default.SoakResult));
            if (args[0] == "--timeline-soak") Win.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
        }
        catch (OperationCanceledException) { }
        catch { }
    }
    void NativeTest()
    {
        if (success < 3 || testPhase > 1)
            return;
        var output = args[1];
        Directory.CreateDirectory(output);
        if (testPhase == 0)
        {
            testPhase = 1;
            canvas.Render(Volatile.Read(ref frame), config, mini);
            canvas.SaveBitmap(Path.Combine(output, "native-detail.bmp"));
            using var process = Process.GetCurrentProcess();
            uint gdiStart = Win.GetGuiResources(process.Handle, 0);
            int assertions = 0;
            for (int i = 0; i < 10000; i++)
                canvas.Render(Volatile.Read(ref frame), config, mini, i % 4);
            if (Win.GetGuiResources(process.Handle, 0) > gdiStart + 2)
                throw new Exception(Strings.T("绘制时 GDI 对象增长"));
            assertions++;
            config.MiniMode = true;
            config.AutoExpand = false;
            compact.Reset();
            RefreshMode();
            ApplyGeometry();
            canvas.Render(Volatile.Read(ref frame), config, mini);
            canvas.SaveBitmap(Path.Combine(output, "native-mini.bmp"));
            foreach (int corner in new[] { 0, 1, 2, 3 })
            {
                config.Corner = corner;
                ApplyGeometry();
                Win.GetWindowRect(hwnd, out var r);
                var screen = CurrentScreen(false);
                if (r.Left < screen.Work.Left || r.Top < screen.Work.Top || r.Right > screen.Work.Right || r.Bottom > screen.Work.Bottom)
                    throw new Exception(Strings.T("窗口越过工作区"));
                assertions++;
            }
            for (int i = 0; i < 100; i++)
            {
                config.UiScale = i % 2 == 0 ? 0.8 : 1.25;
                config.MiniMode = i % 3 == 0;
                compact.Reset();
                RefreshMode();
                ApplyGeometry();
                canvas.Render(Volatile.Read(ref frame), config, mini);
            }
            if (Win.GetGuiResources(process.Handle, 0) > gdiStart + 5)
                throw new Exception(Strings.T("缩放/调整缓冲区时 GDI 对象增长"));
            assertions++;
            var themeCounts=new System.Collections.Generic.List<uint>();
            uint beforeThemes=Win.GetGuiResources(process.Handle,0);
            for(int i=0;i<100;i++)
            {
                config.ColorMode=i%2==0?"light":"dark";
                canvas.Render(Volatile.Read(ref frame),config,mini);
                themeCounts.Add(Win.GetGuiResources(process.Handle,0));
            }
            File.WriteAllText(Path.Combine(output,"theme-gdi.txt"),"before="+beforeThemes+" cycles="+string.Join(",",themeCounts));
            if(themeCounts.Skip(2).Any(count=>count!=themeCounts[1])) throw new Exception("Theme switch leaked GDI resources after palette warmup.");
            assertions++;
            config.MiniMode=false; compact.Reset(); RefreshMode(); ApplyGeometry();
            foreach(string mode in new[]{"light","dark"})
            {
                config.ColorMode=mode; canvas.Render(Volatile.Read(ref frame),config,mini);
                canvas.SaveBitmap(Path.Combine(output,"native-theme-"+mode+".bmp"));
            }
            config.ColorMode="system";
            config.UiScale = 1;
            config.MiniMode = false;
            config.Locked = true;
            config.LockClickThrough = true;
            ApplyGeometry();
            if ((Win.GetStyle(hwnd, -20) & 0x20) == 0 || unlockWindow == IntPtr.Zero)
                throw new Exception(Strings.T("锁定穿透或解锁热点失败"));
            assertions++;
            Win.SendMessage(unlockWindow, 0x0200, IntPtr.Zero, IntPtr.Zero);
            if (hoverButton != 2) throw new Exception(Strings.T("解锁热点悬停反馈失败"));
            assertions++;
            Win.SendMessage(unlockWindow, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            if (hoverButton != -1) throw new Exception(Strings.T("解锁热点悬停未清除"));
            assertions++;
            Win.SendMessage(unlockWindow, 0x0201, IntPtr.Zero, IntPtr.Zero);
            if (config.Locked || unlockWindow != IntPtr.Zero)
                throw new Exception(Strings.T("再次点击解锁失败"));
            assertions++;
            config.AlwaysOnTop = false;
            ApplyGeometry();
            if ((Win.GetStyle(hwnd, -20) & 8) != 0)
                throw new Exception(Strings.T("取消置顶失败"));
            assertions++;
            config.AlwaysOnTop = true;
            ApplyGeometry();
            if ((Win.GetStyle(hwnd, -20) & 8) == 0)
                throw new Exception(Strings.T("启用置顶失败"));
            assertions++;
            var currentScreen = CurrentScreen(false);
            config.MiniMode = true;
            config.MiniDockCorner = false;
            config.MiniHasAnchor = true;
            config.MiniMonitorName = currentScreen.Name;
            config.MiniAnchorX = 0.37;
            config.MiniAnchorY = 0.43;
            compact.Reset();
            RefreshMode();
            ApplyGeometry();
            Win.GetWindowRect(hwnd, out var freelyPlaced);
            double expectedX = currentScreen.Work.Left + 0.37 * (currentScreen.Work.Right - currentScreen.Work.Left - (freelyPlaced.Right - freelyPlaced.Left));
            if (Math.Abs(freelyPlaced.Left - expectedX) > 2)
                throw new Exception(Strings.T("迷你窗自由位置失败"));
            assertions++;
            var moving = new Win.Rect { Left = currentScreen.Work.Left + 5, Top = currentScreen.Work.Top + 5, Right = currentScreen.Work.Left + 5 + pixelWidth, Bottom = currentScreen.Work.Top + 5 + pixelHeight };
            var movingPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Win.Rect>());
            try
            {
                Marshal.StructureToPtr(moving, movingPointer, false);
                dragging = true;
                dragScreens = Screens();
                Win.SendMessage(hwnd, 0x0216, IntPtr.Zero, movingPointer);
                var snapped = Marshal.PtrToStructure<Win.Rect>(movingPointer);
                if (snapped.Left != currentScreen.Work.Left || snapped.Top != currentScreen.Work.Top)
                    throw new Exception(Strings.T("原生移动吸附失败"));
                assertions++;
            }
            finally { dragging = false; dragScreens = null; Marshal.FreeHGlobal(movingPointer); }
            foreach (bool small in new[] { false, true })
            {
                config.MiniMode = small;
                config.AutoExpand = false;
                config.SnapToEdges = false;
                config.Locked = false;
                compact.Reset();
                RefreshMode();
                ApplyGeometry();
                config.Save(ConfigPath);
                Win.GetWindowRect(hwnd, out var origin);
                BeginPointerDrag(new Win.Point { X = origin.Left + 20, Y = origin.Top + 20 });
                if (Win.GetCapture() != hwnd)
                    throw new Exception(Strings.T("拖动没有捕获鼠标"));
                assertions++;
                MovePointerDrag(new Win.Point { X = origin.Left + 87, Y = origin.Top + 61 });
                Win.GetWindowRect(hwnd, out var moved);
                if (moved.Left != origin.Left + 67 || moved.Top != origin.Top + 41)
                    throw new Exception(Strings.T("鼠标拖动没有移动窗口"));
                assertions++;
                EndPointerDrag(true);
                if (dragArmed || Win.GetCapture() == hwnd)
                    throw new Exception(Strings.T("松手没有释放捕获"));
                assertions++;
                if (small && !config.MiniHasAnchor || !small && !config.HasAnchor)
                    throw new Exception(Strings.T("拖动没有记住位置"));
                assertions++;
            }
            config.SnapToEdges = true;
            config.MiniMode = false;
            config.Locked = true;
            ApplyGeometry();
            canvas.Render(Volatile.Read(ref frame), config, mini);
            canvas.SaveBitmap(Path.Combine(output, "native-locked.bmp"));
            config.Locked = false;
            ApplyGeometry();
            int logicalWidth = (int)Math.Round(pixelWidth / drawScale);
            for (int button = 0; button < 4; button++)
            {
                int x = (int)((logicalWidth - 79 + button * 22) * drawScale);
                int y = (int)(14 * drawScale);
                Win.SendMessage(hwnd, 0x0200, IntPtr.Zero, new IntPtr((x & 0xffff) | (y << 16)));
                if (hoverButton != button) throw new Exception(Strings.T("顶部按钮悬停区未对齐"));
                assertions++;
            }
            Win.SendMessage(hwnd, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            if (hoverButton != -1) throw new Exception(Strings.T("顶部按钮悬停未清除"));
            assertions++;
            canvas.Render(Volatile.Read(ref frame), config, mini);
            canvas.SaveBitmap(Path.Combine(output, "native-detail.bmp"));
            config.Visible = false;
            ApplyVisibility();
            OpenSettings(true);
            File.WriteAllText(Path.Combine(output, "native-test.txt"), $"PASS: {assertions} assertions, direct mouse drag in both modes, capture/release and saved position, 10000 paints, 100 resize/mode cycles, four corners; GDI start={gdiStart}, end={Win.GetGuiResources(process.Handle, 0)}\n");
        }
        else if (watch.Elapsed.TotalSeconds - settingsStarted > 20)
        {
            testPhase = 2;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    while (settingsProcess != null && !settingsProcess.HasExited)
                    {
                        settingsProcess.Refresh();
                        if (settingsProcess.MainWindowHandle != IntPtr.Zero)
                            settingsProcess.CloseMainWindow();
                        await Task.Delay(250, timeout.Token);
                    }
                    File.AppendAllText(Path.Combine(output, "native-test.txt"), "PASS: settings process exited after window close\n");
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(output, "native-test.txt"), "FAIL: settings exit: " + ex.Message + "\n"); try { if (settingsProcess != null && !settingsProcess.HasExited) { settingsProcess.Kill(); await settingsProcess.WaitForExitAsync(); } } catch { } }
                finally { Win.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero); }
            });
        }
    }
    void Close(string reason = "window-close")
    {
        if (closing)
            return;
        closing = true;
        try
        {
            File.WriteAllText(Path.Combine(root, "last-exit.json"), JsonSerializer.Serialize(new ExitStatus(DateTimeOffset.Now, reason, success, failures), MonitorJson.Default.ExitStatus));
        }
        catch { }
        stop.Cancel();
        Win.DestroyWindow(hwnd);
    }
    public void Dispose()
    {
        activationWait?.Unregister(null);
        stop.Cancel();
        settingsProcess?.Dispose();
        tip?.Dispose();
        if (icon.Icon != IntPtr.Zero)
        {
            Win.DestroyIcon(icon.Icon);
            icon.Icon = IntPtr.Zero;
        }
        canvas.Dispose();
        try
        {
            if (File.Exists(LivePath))
                File.Delete(LivePath);
        }
        catch { }
        stop.Dispose();
        GC.KeepAlive(procedure);
    }
}
public static class Entry
{
    [STAThread]
    public static void Main(string[] args)
    {
        string suffix = args.Length == 0 ? "" : args[0] switch { "--native-test" => ".NativeTest", "--timeline-soak" => ".TimelineSoak", _ => "" };
        string name = @"Local\ProxyTrafficFloat.User." + Environment.UserName + suffix;
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        using var mutex = new Mutex(false, name);
        bool owns;
        try
        {
            owns = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            activate.Set();
            return;
        }
        try
        {
            using var monitor = new NativeMonitor(args, activate);
            monitor.Run();
        }
        catch (Exception ex) { Win.MessageBox(IntPtr.Zero, Strings.T(ex.Message), Strings.T("代理流量"), 0x10); }
        finally { mutex.ReleaseMutex(); }
    }
}
