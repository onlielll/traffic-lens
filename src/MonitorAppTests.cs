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

public sealed partial class MonitorApp
{
    async Task CapturePagesAsync(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "settings-test.txt"), "RUNNING: UI verification has not completed.\n");
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
        File.WriteAllText(Path.Combine(dataRoot, "live.json"), JsonSerializer.Serialize(new { connected = true, status = "内核在线 · 本机命名管道", source = "命名管道", at = DateTimeOffset.UtcNow, latency = new NodeLatency("example-node", 420, DateTimeOffset.UtcNow, new[] { 86, 92, 110, 104, 180, 350, 420 }, true) }));
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
            await settings.VerifyTimelineForTest(output);
            await settings.VerifyStatisticsForTest(output);
            settings.PrepareQuotaForTest();
            await settings.VerifyReuseForTest(output);
            await settings.VerifyScrollingAndSlidersAsync(output);
            foreach (string language in new[] { "zh-CN", "en" })
            {
                settings.SetLanguage(language);
                foreach (bool small in new[] { false, true })
                {
                    settings.Width = small ? 520 : 820;
                    settings.Height = small ? 360 : 650;
                    for (int i = 0; i < 4; i++)
                    {
                        settings.PreparePageCaptureForTest(i);
                        if (i == 2) settings.SelectStatisticsView(0, false);
                        await Task.Delay(240);
                        SavePage($"settings-{language}{(small ? "-small" : "")}-{i}.png");
                        if (i == 3)
                        {
                            await settings.PrepareUpdatePanelForTest();
                            SavePage($"updates-{language}{(small ? "-small" : "")}.png");
                        }
                        if (i == 2)
                        {
                            settings.ShowQuotaForTest(); await Task.Delay(240);
                            SavePage($"quota-{language}{(small ? "-small" : "")}.png");
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
            await settings.PreviewLowTrafficForTest(false); await Task.Delay(250); SavePage("timeline-low-burst.png");
            await settings.PreviewLowTrafficForTest(true); await Task.Delay(250); SavePage("timeline-low-detail.png");
            settings.SelectPage(0);
            settings.OpenAppearanceForTest(); await Task.Delay(220);
            if(settings.AppearancePreviewForTest is FrameworkElement appearance)
            {
                var preview=new RenderTargetBitmap((int)Math.Ceiling(appearance.ActualWidth+20),(int)Math.Ceiling(appearance.ActualHeight+20),96,96,PixelFormats.Pbgra32);
                preview.Render(appearance); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview));
                using var appearanceStream=File.Create(Path.Combine(output,"appearance-picker.png")); png.Save(appearanceStream);
            }
            settings.CloseAppearanceForTest();
            await settings.VerifyClearOrderingAsync(output);
            File.WriteAllText(Path.Combine(output, "settings-test.txt"), "PASS: Chinese/English, four pages at normal and compact sizes, instant language change, toggle animation, isolated example data\n");
            Environment.ExitCode = 0;
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
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

}
