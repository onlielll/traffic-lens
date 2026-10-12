using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace ProxyTrafficFloat;

/// <summary>Lives only in the settings process. Reuses one plot and the existing settings refresh.</summary>
public sealed class TimelinePanel : StackPanel, IDisposable
{
    readonly MonitorApp app;
    readonly ComboBox period = new() { MinWidth = 102, Margin = new Thickness(0, 0, 8, 8) };
    readonly ComboBox metric = new() { MinWidth = 112, Margin = new Thickness(0, 0, 8, 8) };
    readonly ComboBox target = new() { MinWidth = 180, MaxWidth = 320, IsEditable = true, IsTextSearchEnabled = true, Margin = new Thickness(0, 0, 0, 8) };
    readonly PlotModel model = new();
    readonly PlotView plot;
    readonly LineSeries down = new(), up = new();
    readonly LocalTimeAxis x = new() { Position = AxisPosition.Bottom, StringFormat = "HH:mm" };
    readonly LinearAxis y = new() { Position = AxisPosition.Left, Minimum = 0, AbsoluteMinimum = 0, MinimumPadding = 0, MaximumPadding = 0.12 };
    readonly TextBlock note = Ui.Label("", 10.5, Ui.Muted);
    readonly TextBlock empty = Ui.Label("", 12, Ui.Muted);
    readonly Action changed;
    bool syncing;
    string selectedTarget = "";
    DateTime refreshed = DateTime.MinValue;

    public TimelinePanel(MonitorApp app, Action changed)
    {
        this.app = app; this.changed = changed;
        var selectors = new WrapPanel();
        selectors.Children.Add(period); selectors.Children.Add(metric); selectors.Children.Add(target);
        Children.Add(selectors);
        down.Color = OxyColor.Parse("#62D8BD"); up.Color = OxyColor.Parse("#B1A3F9");
        down.StrokeThickness = up.StrokeThickness = 2.4;
        down.CanTrackerInterpolatePoints = up.CanTrackerInterpolatePoints = false;
        model.Series.Add(down); model.Series.Add(up);
        foreach (var axis in new Axis[] { x, y })
        {
            axis.TextColor = OxyColor.Parse("#94A2BA");
            axis.TitleColor = axis.TextColor;
            axis.FontSize = 10;
            axis.AxislineColor = OxyColor.Parse("#2A3546");
            axis.MajorGridlineColor = OxyColor.Parse("#2A3546");
            axis.MajorGridlineStyle = LineStyle.Solid;
            axis.MinorGridlineStyle = LineStyle.None;
            axis.TickStyle = TickStyle.None;
            axis.IsPanEnabled = axis.IsZoomEnabled = false;
            model.Axes.Add(axis);
        }
        model.Background = OxyColor.Parse("#18202D");
        model.PlotAreaBorderColor = OxyColors.Undefined;
        model.TextColor = OxyColor.Parse("#EEF3FB");
        var controller = new PlotController();
        controller.UnbindMouseWheel(); // Let the settings page scroll normally.
        plot = new PlotView { Model = model, Controller = controller, Background = Ui.Panel, Height = 235 };
        var canvas = new Grid();
        canvas.Children.Add(plot);
        empty.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        empty.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        empty.TextAlignment = TextAlignment.Center;
        empty.Margin = new Thickness(24);
        canvas.Children.Add(empty);
        Children.Add(canvas);
        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var d = Ui.Label("↓ 下载", 11, Ui.Down); d.Margin = new Thickness(0, 0, 18, 0);
        legend.Children.Add(d); legend.Children.Add(Ui.Label("↑ 上传", 11, Ui.Up));
        Children.Add(legend); Children.Add(note);
        System.Windows.Automation.AutomationProperties.SetName(plot, Strings.T("上下行时间曲线"));
        period.SelectionChanged += (_, _) => { if (syncing) return; app.Config.TimelineRange = Math.Max(0, period.SelectedIndex); changed(); Refresh(true); };
        metric.SelectionChanged += (_, _) => { if (syncing) return; app.Config.TimelineMetric = Math.Max(0, metric.SelectedIndex); changed(); Refresh(true); };
        target.SelectionChanged += (_, _) => { if (!syncing) SelectTarget(target.SelectedIndex == 0 ? "" : target.SelectedItem as string ?? ""); };
        target.LostKeyboardFocus += (_, _) => { if (!syncing) SelectTarget(target.Text == Strings.T("全部代理流量") ? "" : target.Text.Trim().TrimEnd('.').ToLowerInvariant()); };
        target.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { SelectTarget(target.Text == Strings.T("全部代理流量") ? "" : target.Text.Trim().TrimEnd('.').ToLowerInvariant()); e.Handled = true; } };
        SyncChoices();
    }

    public void SelectTarget(string value)
    {
        selectedTarget = value;
        syncing = true;
        target.Text = value.Length == 0 ? Strings.T("全部代理流量") : value;
        syncing = false;
        Refresh(true);
    }

    void SyncChoices()
    {
        syncing = true;
        period.ItemsSource = new[] { "近 1 小时", "今天", "近 7 天" }.Select(Strings.T).ToArray();
        metric.ItemsSource = new[] { "平均速率", "每分钟用量" }.Select(Strings.T).ToArray();
        period.SelectedIndex = app.Config.TimelineRange;
        metric.SelectedIndex = app.Config.TimelineMetric;
        syncing = false;
    }

    public void UpdateTargets(IEnumerable<DailyRow> rows)
    {
        syncing = true;
        var names = rows.Select(row => row.Target).Take(200).ToList();
        if (selectedTarget.Length > 0 && !names.Contains(selectedTarget)) names.Add(selectedTarget);
        target.ItemsSource = new[] { Strings.T("全部代理流量") }.Concat(names).ToArray();
        target.SelectedItem = selectedTarget.Length == 0 ? Strings.T("全部代理流量") : selectedTarget;
        target.Text = selectedTarget.Length == 0 ? Strings.T("全部代理流量") : selectedTarget;
        syncing = false;
    }

    public void Refresh(bool force = false)
    {
        if (!force && DateTime.UtcNow - refreshed < TimeSpan.FromSeconds(5)) return;
        SyncChoices();
        var end = DateTimeOffset.UtcNow;
        var first = app.Config.TimelineRange switch
        {
            1 => new DateTimeOffset(DateTime.Today),
            2 => new DateTimeOffset(DateTime.Today.AddDays(-6)),
            _ => DateTimeOffset.FromUnixTimeSeconds(end.AddHours(-1).ToUnixTimeSeconds() / 60 * 60)
        };
        int step = app.Config.TimelineRange == 2 ? 600 : 60;
        var data = app.Store.QueryTimeline(first, end, selectedTarget, step);
        bool speed = app.Config.TimelineMetric == 0;
        double Value(TimelinePoint point, bool download)
        {
            double bytes = download ? point.Download : point.Upload;
            return bytes / (speed ? point.Seconds : point.Minutes);
        }
        double maximum = data.Count == 0 ? 0 : data.Max(point => Math.Max(Value(point, true), Value(point, false)));
        double unit = maximum >= 1024 * 1024 ? 1024 * 1024 : 1024;
        string suffix = (unit == 1024 ? "KB" : "MB") + (speed ? "/s" : "");
        y.Title = Strings.T(speed ? "平均速率" : "每分钟用量") + " · " + suffix;
        down.Title = Strings.T("下载"); up.Title = Strings.T("上传");
        down.TrackerFormatString = up.TrackerFormatString = "{0}\n{2:yyyy-MM-dd HH:mm}\n{4:0.##} " + suffix;
        down.Points.Clear(); up.Points.Clear();
        long? previous = null;
        foreach (var point in data)
        {
            if (previous != null && point.Minute - previous > step)
            {
                down.Points.Add(DataPoint.Undefined); up.Points.Add(DataPoint.Undefined);
            }
            double time = DateTimeAxis.ToDouble(DateTimeOffset.FromUnixTimeSeconds(point.Minute).UtcDateTime);
            down.Points.Add(new(time, Value(point, true) / unit));
            up.Points.Add(new(time, Value(point, false) / unit));
            previous = point.Minute;
        }
        x.Minimum = DateTimeAxis.ToDouble(first.UtcDateTime);
        x.Maximum = DateTimeAxis.ToDouble(end.UtcDateTime);
        x.StringFormat = app.Config.TimelineRange == 2 ? "MM-dd HH:mm" : "HH:mm";
        empty.Text = Strings.T(app.Config.RecordTimeline ? "等待时间记录\n完成一个分钟区间后，曲线会出现在这里。" : "时间记录未开启\n开启后可查看流量随时间的变化。" );
        empty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        note.Text = Strings.T(app.Config.RecordTimeline ? "正在记录 · 按分钟刷新 · 保留 7 天" : "记录已暂停 · 已有历史仍可查看") + "\n" +
            Strings.T(step == 600 ? "本地时间 · 每点合并 10 分钟 · 空白表示未监测" : "本地时间 · 每点 1 分钟 · 空白表示未监测") + "\n" +
            Strings.T("速率按实际监测时长计算；每分钟用量按已记录分钟平均。");
        System.Windows.Automation.AutomationProperties.SetName(plot, Strings.T("上下行时间曲线"));
        var active = data.Where(point => point.First != 0).ToArray();
        if (active.Length > 0)
            note.Text += "\n" + Strings.Format("观测区间：{0} — {1}",
                DateTimeOffset.FromUnixTimeMilliseconds(active.Min(point => point.First)).LocalDateTime.ToString("MM-dd HH:mm:ss"),
                DateTimeOffset.FromUnixTimeMilliseconds(active.Max(point => point.Last)).LocalDateTime.ToString("MM-dd HH:mm:ss"));
        model.ResetAllAxes();
        model.InvalidatePlot(true);
        refreshed = DateTime.UtcNow;
    }
    public void VerifyForTest(string output)
    {
        var utc = DateTimeOffset.UtcNow;
        var shown = (DateTime)x.GetValue(DateTimeAxis.ToDouble(utc.UtcDateTime));
        if (Math.Abs((shown - utc.LocalDateTime).TotalMilliseconds) > 2) throw new InvalidOperationException("Timeline timezone differs from local time.");
        target.ApplyTemplate();
        if (target.Template.FindName("PART_EditableTextBox", target) is not TextBox) throw new InvalidOperationException("Target input is not editable.");
        SelectTarget("downloads.example");
        if (down.Points.Count == 0 || up.Points.Any(point => !double.IsNaN(point.Y) && point.Y != 0)) throw new InvalidOperationException("Timeline target filter failed.");
        SelectTarget("");
        for (int i = 0; i < 100; i++) Refresh(true);
        if (!ReferenceEquals(plot.Model, model) || down.Points.Count > 1500 || up.Points.Count > 1500) throw new InvalidOperationException("Timeline plot retained additional models or points.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "timeline-ui-test.txt"), "PASS: local timestamps, editable target, target filter, 100 repeated updates reuse one plot with bounded points\n");
    }
    public void Dispose() { plot.Model = null; down.Points.Clear(); up.Points.Clear(); }
}

// DateTimeAxis reconstructs an unspecified DateTime. Mark it UTC before converting for labels and trackers.
public sealed class LocalTimeAxis : DateTimeAxis
{
    public override object GetValue(double value) => DateTime.SpecifyKind(ConvertToDateTime(value), DateTimeKind.Utc).ToLocalTime();
    protected override string FormatValueOverride(double value) => ((DateTime)GetValue(value)).ToString(StringFormat ?? "HH:mm");
}
