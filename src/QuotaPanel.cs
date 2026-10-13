using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProxyTrafficFloat;

// Settings-only view: no timer, collection cache or chart dependency in the resident.
public sealed class QuotaPanel : StackPanel
{
    readonly MonitorApp app;
    readonly TextBlock duration = Ui.Label("填写剩余流量", 26);
    readonly TextBlock remaining = Ui.Label("", 13, Ui.Muted);
    readonly TextBlock trend = Ui.Label("", 11, Ui.Muted);
    readonly TextBlock feedback = Ui.Label("", 11, Ui.Muted);
    readonly TextBox input = new() { Width = 96, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    DateTime refreshed, shownQuota;
    string language = "";

    public QuotaPanel(MonitorApp app)
    {
        this.app = app;
        var heading = Ui.Label("额度与趋势", 15);
        heading.FontWeight = FontWeights.SemiBold;
        Children.Add(heading);
        var metrics = new WrapPanel { Margin = new Thickness(0, 10, 0, 2) };
        duration.FontWeight = FontWeights.SemiBold;
        duration.Foreground = Ui.Down;
        duration.Tag = null;
        metrics.Children.Add(duration);
        remaining.Margin = new Thickness(12, 12, 0, 0);
        metrics.Children.Add(remaining);
        Children.Add(metrics);
        Children.Add(trend);
        Children.Add(Ui.Label("填写套餐当前剩余额度；此后仅扣除本机监测用量。", 11, Ui.Muted));
        var editor = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(input, "RemainingQuotaGB");
        System.Windows.Automation.AutomationProperties.SetName(input, Strings.T("剩余流量（GB）"));
        input.ToolTip = Strings.T("GB 使用十进制单位：1 GB = 1,000 MB。");
        editor.Children.Add(input);
        var unit = Ui.Label("GB", 11, Ui.Muted);
        unit.Margin = new Thickness(0, 10, 12, 0);
        editor.Children.Add(unit);
        var update = Ui.Button("更新额度", (_, _) => UpdateQuota());
        update.SetResourceReference(StyleProperty, "AccentButton");
        update.Padding = new Thickness(12, 8, 12, 8);
        editor.Children.Add(update);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { UpdateQuota(); e.Handled = true; } };
        Children.Add(editor);
        Children.Add(feedback);
        Children.Add(Ui.Label("按最近 7 个有记录日估算，不含今天。上传和下载合计，离线消耗未计入。", 11, Ui.Muted));
    }

    void UpdateQuota()
    {
        if (!double.TryParse(input.Text, out var gb) || !double.IsFinite(gb) || gb < 0 || gb > 1048576)
        {
            feedback.Text = Strings.T("请输入有效的剩余流量。零表示额度已用完。");
            feedback.Foreground = Ui.PaletteBrush("Danger");
            return;
        }
        try
        {
            long observed = app.Store.ObservedBytes();
            app.Config.QuotaGB = gb;
            app.Config.QuotaBaselineBytes = observed;
            app.Config.QuotaUpdatedUtc = DateTime.UtcNow;
            app.Save();
            feedback.Text = "";
            Refresh(true);
            app.Apply();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Write(app.DataDirectory, "quota-update", error);
            feedback.Text = Strings.T("额度未保存，请重试。");
            feedback.Foreground = Ui.PaletteBrush("Danger");
        }
    }

    public void Refresh(bool force = false)
    {
        if (!force && shownQuota == app.Config.QuotaUpdatedUtc && language == Strings.Language && DateTime.UtcNow - refreshed < TimeSpan.FromSeconds(5)) return;
        var config = app.Config;
        var estimate = config.HasQuota ? QuotaForecast.Calculate(config, app.Store.ObservedBytes(), app.Store.RecentDailyTrend(DateTime.Today)) : null;
        duration.Text = estimate?.DurationText ?? Strings.T("填写剩余流量");
        remaining.Text = estimate is { } value ? Strings.Format("剩余 {0:0.##} GB", value.RemainingBytes / QuotaForecast.BytesPerGB) : "";
        trend.Text = estimate is { RecordedDays: > 0 } available ? Strings.Format("日均 {0:0.##} GB · {1} 个记录日", available.DailyBytes / QuotaForecast.BytesPerGB, available.RecordedDays) : Strings.T("至少需要一个往日记录日才能估算。");
        if (shownQuota != config.QuotaUpdatedUtc && !input.IsKeyboardFocusWithin)
            input.Text = estimate is { } current ? (current.RemainingBytes / QuotaForecast.BytesPerGB).ToString("0.##") : "";
        shownQuota = config.QuotaUpdatedUtc;
        language = Strings.Language;
        System.Windows.Automation.AutomationProperties.SetName(input, Strings.T("剩余流量（GB）"));
        input.ToolTip = Strings.T("GB 使用十进制单位：1 GB = 1,000 MB。");
        refreshed = DateTime.UtcNow;
    }

    public void VerifyCommitForTest()
    {
        void Enter(string value)
        {
            input.Text = value;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        }
        Enter("24");
        if (app.Config.QuotaGB != 24 || app.Config.QuotaBaselineBytes != app.Store.ObservedBytes()) throw new InvalidOperationException("Quota input did not commit its snapshot.");
        var saved = app.Config.QuotaUpdatedUtc;
        Enter("-1");
        if (app.Config.QuotaUpdatedUtc != saved || app.Config.QuotaGB != 24) throw new InvalidOperationException("Invalid quota input changed the snapshot.");
        app.Store.Add(new[] { new Increment(DateTime.Today, "downloads.example", 1_500_000_000, 500_000_000) });
        Refresh(true);
        if (duration.Text != Strings.Format("约 {0:0} 天", 22)) throw new InvalidOperationException("Observed usage did not reduce the displayed allowance.");
        Enter("48");
    }
    public string DurationForTest => duration.Text;
}