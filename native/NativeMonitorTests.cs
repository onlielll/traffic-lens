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

public sealed partial class NativeMonitor
{
    Task? soak, verification;
    bool childVerified;
    int testPhase;
    double settingsStarted;
    async Task SoakAsync(string output, int duration)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var writer = new StreamWriter(output) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakStart("start", DateTimeOffset.Now, duration, "native-gdi"), MonitorJson.Default.SoakStart));
            var clock = Stopwatch.StartNew();
            var trend = new ResourceTrend();
            try
            {
            while (clock.Elapsed.TotalSeconds < duration && !stop.IsCancellationRequested)
            {
                await Task.Delay(Math.Min(30000, Math.Max(1, (int)((duration - clock.Elapsed.TotalSeconds) * 1000))), stop.Token);
                using var process = Process.GetCurrentProcess();
                if (clock.Elapsed.TotalSeconds >= Math.Min(120, duration * 0.2))
                    trend.Add(process.PrivateMemorySize64);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakSample("sample", clock.Elapsed.TotalSeconds, success, failures, process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false), process.HandleCount, Win.GetGuiResources(process.Handle, 0), Win.GetGuiResources(process.Handle, 1)), MonitorJson.Default.SoakSample));
            }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            bool enough = clock.Elapsed.TotalSeconds >= duration && success >= Math.Max(1, (int)(duration * 0.9));
            bool passed = enough && failures == 0 && trend.Stable == true;
            exitCode = passed ? 0 : 1;
            string result = !enough || trend.Stable == null ? "incomplete" : passed ? "complete" : "failed";
            await writer.WriteLineAsync(JsonSerializer.Serialize(new SoakResult(result, clock.Elapsed.TotalSeconds, success, failures, trend.Stable, trend.Growth), MonitorJson.Default.SoakResult));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { exitCode = 1; Diagnostics.Write(root, "soak-failed", error); }
        finally { if (args[0] == "--timeline-soak") Win.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero); }
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
            int assertions = VerifyViewportForTest(output);
            if (!Win.Shell_NotifyIcon(2, ref icon)) throw new Exception("Could not remove the test tray icon.");
            trayReady = false;
            icon.Flags = 4;
            Win.SendMessage(hwnd, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
            if (!trayReady || icon.Flags != 7) throw new Exception("TaskbarCreated did not restore the tray icon and callback.");
            assertions++;
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
            Win.GetWindowRect(hwnd, out var ownerBefore);
            Win.GetWindowRect(unlockWindow, out var hotspotBefore);
            Win.SetWindowPos(hwnd, IntPtr.Zero, ownerBefore.Left + 7, ownerBefore.Top + 5, 0, 0, 0x0015);
            Win.GetWindowRect(unlockWindow, out var hotspotAfter);
            if (hotspotAfter.Left - hotspotBefore.Left != 7 || hotspotAfter.Top - hotspotBefore.Top != 5)
                throw new Exception("System move left the unlock hotspot behind.");
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
            int heightBefore = pixelHeight;
            config.ShowQuotaForecast = true;
            config.QuotaUpdatedUtc = DateTime.UtcNow;
            config.QuotaGB = 48;
            ApplyGeometry();
            if (Math.Abs(pixelHeight - heightBefore - 19 * drawScale) > 1) throw new Exception($"Quota row height was not reserved: before={heightBefore}, after={pixelHeight}, scale={drawScale}, ui={config.UiScale}, node={config.ShowNodeLatency}, drag={dragArmed}/{dragging}.");
            assertions++;
            var preview = Volatile.Read(ref frame) with { QuotaText = Strings.Format("预计可用 · {0}", new QuotaForecast(48_000_000_000, 1_000_000_000, 7).DurationText) };
            canvas.Render(preview, config, mini);
            canvas.SaveBitmap(Path.Combine(output, "native-quota.bmp"));
            uint quotaGdi = Win.GetGuiResources(process.Handle, 0);
            for (int i = 0; i < 10000; i++) canvas.Render(preview, config, mini);
            if (Win.GetGuiResources(process.Handle, 0) != quotaGdi) throw new Exception("Quota painting retained GDI resources.");
            assertions++;
            config.MiniMode = true; compact.Reset(); RefreshMode(); ApplyGeometry();
            if (pixelHeight != (int)Math.Round(58 * drawScale)) throw new Exception("Quota changed mini mode size.");
            assertions++;
            config.MiniMode = false; config.ShowQuotaForecast = false; config.QuotaUpdatedUtc = default;
            compact.Reset(); RefreshMode(); ApplyGeometry();
            config.Visible = false;
            ApplyVisibility();
            OpenSettings(true);
            File.WriteAllText(Path.Combine(output, "native-test.txt"), $"PASS: {assertions} assertions, direct mouse drag in both modes, capture/release and saved position, 10000 paints, 100 resize/mode cycles, four corners; GDI start={gdiStart}, end={Win.GetGuiResources(process.Handle, 0)}\n");
        }
        else if (watch.Elapsed.TotalSeconds - settingsStarted > 20)
        {
            testPhase = 2;
            verification = Task.Run(async () =>
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
                    if (settingsProcess == null || settingsProcess.ExitCode != 0)
                        throw new InvalidOperationException("Settings verification process failed.");
                    File.AppendAllText(Path.Combine(output, "native-test.txt"), "PASS: settings process exited after window close\n");
                    childVerified = true;
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(output, "native-test.txt"), "FAIL: settings exit: " + ex.Message + "\n"); try { if (settingsProcess != null && !settingsProcess.HasExited) { settingsProcess.Kill(); await settingsProcess.WaitForExitAsync(); } } catch { } }
                finally { Win.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero); }
            });
        }
    }
    int VerifyViewportForTest(string output)
    {
        int assertions = 0;
        var original = config.Copy();
        foreach (bool compactMode in new[] { false, true })
        {
            config.MiniMode = compactMode; config.AutoExpand = false; config.Locked = false; config.ClickThrough = false;
            compact.Reset(); RefreshMode(); ApplyGeometry();
            foreach (int corner in new[] { 13, 14, 16, 17 })
            {
                config.UiScale = 1; ApplyGeometry();
                Win.GetWindowRect(hwnd, out var bounds);
                var point = new Win.Point { X = corner is 13 or 16 ? bounds.Left + 5 : bounds.Right - 5, Y = corner is 13 or 14 ? bounds.Top + 5 : bounds.Bottom - 5 };
                if (ResizeHit(point.X - bounds.Left, point.Y - bounds.Top) != corner) throw new Exception("Corner hit test failed.");
                BeginPointerResize(corner, point);
                MovePointerResize(new Win.Point { X = point.X + (corner is 13 or 16 ? -22 : 22), Y = point.Y + (corner is 13 or 14 ? -10 : 10) });
                EndPointerDrag(false);
                if (config.UiScale <= 1 || Settings.Load(ConfigPath).UiScale != config.UiScale) throw new Exception("Corner resize was not saved.");
                assertions++;
            }
            config.Locked = true;
            if (ResizeHit(5, 5) != 0) throw new Exception("Locked overlay can resize.");
            config.Locked = false; assertions++;
        }
        var monitor = new Win.MonitorInfo { Size = Marshal.SizeOf<Win.MonitorInfo>() };
        Win.GetMonitorInfo(Win.MonitorFromWindow(hwnd, 2), ref monitor);
        Win.WindowProc testProcedure = Win.DefWindowProc;
        var instance = Win.GetModuleHandle(null);
        var cls = new Win.WindowClass { Size = (uint)Marshal.SizeOf<Win.WindowClass>(), Name = "TrafficLens.FullscreenTest", Proc = testProcedure, Instance = instance };
        Win.RegisterClassEx(ref cls);
        var test = Win.CreateWindowEx(0x08080088, cls.Name, "test", 0x80000000, monitor.Monitor.Left, monitor.Monitor.Top,
            monitor.Monitor.Right - monitor.Monitor.Left, monitor.Monitor.Bottom - monitor.Monitor.Top, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        try
        {
            Win.SetLayeredWindowAttributes(test, 0, 0, 2); Win.ShowWindow(test, 4);
            if (!FullscreenWindow(test)) throw new Exception("Fullscreen detection missed a monitor-sized native window.");
            Win.SetWindowPos(test, IntPtr.Zero, 0, 0, 100, 100, 0x0016);
            if (FullscreenWindow(test)) throw new Exception("Normal window classified as fullscreen.");
            config.Visible = true; fullscreenHidden = true; ApplyVisibility();
            if (Win.IsWindowVisible(hwnd) || !config.Visible) throw new Exception("Fullscreen hide changed user visibility.");
            fullscreenHidden = false; ApplyVisibility();
            if (!Win.IsWindowVisible(hwnd)) throw new Exception("Fullscreen exit did not restore overlay.");
            assertions += 4;
        }
        finally { Win.DestroyWindow(test); Win.UnregisterClass(cls.Name, instance); GC.KeepAlive(testProcedure); }
        config = original.Copy(); config.ShowNodeLatency = true; ApplyGeometry();
        var example = Volatile.Read(ref frame) with { Latency = new NodeLatency("example-node", 410, DateTimeOffset.UtcNow, new[] { 90, 85, 150, 110, 360, 410 }, true), LatencyCaption = "example-node · 410 ms" };
        canvas.Render(example, config, mini);
        canvas.SaveBitmap(Path.Combine(output, "native-latency.bmp"));
        using var process = Process.GetCurrentProcess();
        uint before = Win.GetGuiResources(process.Handle, 0);
        for (int i = 0; i < 10000; i++) canvas.Render(example, config, mini);
        if (Win.GetGuiResources(process.Handle, 0) != before) throw new Exception("Latency sparkline leaks GDI resources.");
        config = original; ApplyGeometry(); ApplyVisibility();
        File.WriteAllText(Path.Combine(output, "viewport-test.txt"), $"PASS: {assertions} fullscreen/corner/locked checks; 10000 node-trend paints without GDI growth\n");
        return assertions + 1;
    }
}
