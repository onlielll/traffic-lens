using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace ProxyTrafficFloat;

/// <summary>One reusable plot in the short-lived settings process. Only visible, aggregated points are loaded.</summary>
public sealed class TimelinePanel : StackPanel, IDisposable
{
    readonly MonitorApp app;
    readonly Action changed;
    readonly ComboBox period = new() { MinWidth = 112, Margin = new Thickness(0, 0, 8, 8) };
    readonly ComboBox metric = new() { MinWidth = 130, Margin = new Thickness(0, 0, 8, 8) };
    readonly ComboBox target = new() { MinWidth = 180, MaxWidth = 320, IsEditable = true, IsTextSearchEnabled = true, Margin = new Thickness(0, 0, 0, 8) };
    readonly PlotModel model = new();
    readonly PlotView plot;
    readonly LineSeries down = new(), up = new();
    readonly ScatterSeries downDot = new() { MarkerType = MarkerType.Circle, MarkerSize = 4 }, upDot = new() { MarkerType = MarkerType.Circle, MarkerSize = 4 };
    readonly LocalTimeAxis x = new() { Position = AxisPosition.Bottom };
    readonly LinearAxis y = new() { Position = AxisPosition.Left, Minimum = 0, AbsoluteMinimum = 0, MinimumPadding = 0, MaximumPadding = 0.12 };
    readonly LineAnnotation cursor = new() { Type = LineAnnotationType.Vertical, LineStyle = LineStyle.Dash, StrokeThickness = 1, X = double.NaN };
    readonly LineAnnotation startLine = new() { Type = LineAnnotationType.Vertical, StrokeThickness = 1.5 }, endLine = new() { Type = LineAnnotationType.Vertical, StrokeThickness = 1.5 };
    readonly RectangleAnnotation band = new() { Layer = AnnotationLayer.BelowSeries, StrokeThickness = 0 };
    readonly TextBlock note = Ui.Label("", 11.5, Ui.Muted), empty = Ui.Label("", 12, Ui.Muted), unitLabel = Ui.Label("", 12, Ui.Muted);
    readonly TextBlock selection = Ui.Label("", 12), pointerTime = Ui.Label("", 12, Ui.Muted), pointerDown = Ui.Label("", 14, Ui.Down), pointerUp = Ui.Label("", 14, Ui.Up);
    readonly Canvas overlay = new() { IsHitTestVisible = false };
    readonly Border pointer;
    readonly Slider pan = new() { Minimum = 0, Visibility = Visibility.Collapsed, Margin = new Thickness(44, 2, 10, 3) };
    readonly Button clear;
    readonly Button overview, detail;
    bool detailScale;
    long hoverAt;
    List<TimelinePoint> data = new();
    string selectedTarget = "", suffix = "KB/s";
    bool syncing, full = true;
    long fullFirst, fullLast, viewFirst, viewLast;
    long? selectedFirst, selectedLast;
    int step = 1, paletteVersion;
    double unit = 1024;
    Point? hoverPoint;
    DateTime refreshed = DateTime.MinValue;

    public TimelinePanel(MonitorApp app, Action changed)
    {
        this.app = app;
        this.changed = changed;
        var selectors = new WrapPanel();
        selectors.Children.Add(period);
        selectors.Children.Add(metric);
        selectors.Children.Add(target);
        Children.Add(selectors);
        var scaleSelector = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        overview = Ui.Button("突发", (_, _) => { detailScale = false; Refresh(true); });
        detail = Ui.Button("细节", (_, _) => { detailScale = true; Refresh(true); });
        foreach (var button in new[] { overview, detail })
        {
            button.Padding = new Thickness(9, 5, 9, 5);
            button.Margin = new Thickness(0, 0, 3, 0);
        }
        overview.ToolTip = Strings.T("固定最低 1 MB/s 的纵轴，便于辨认突发流量。");
        detail.ToolTip = Strings.T("按当前数据缩放纵轴，查看低速流量。");
        scaleSelector.Children.Add(overview);
        scaleSelector.Children.Add(detail);
        unitLabel.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(unitLabel);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(scaleSelector);
        void Tool(string label, Action action)
        {
            var button = Ui.Button(label, (_, _) => action());
            button.Padding = new Thickness(9, 5, 9, 5);
            toolbar.Children.Add(button);
        }
        Tool("−", () => Zoom(2, 0.5));
        Tool("+", () => Zoom(0.5, 0.5));
        Tool("完整范围", () => { full = true; Refresh(true); });
        Children.Add(toolbar);
        var spline = new ShapePreservingSpline();
        down.InterpolationAlgorithm = up.InterpolationAlgorithm = spline;
        down.StrokeThickness = up.StrokeThickness = 2.8;
        down.CanTrackerInterpolatePoints = up.CanTrackerInterpolatePoints = false;
        model.Series.Add(down);
        model.Series.Add(up);
        model.Series.Add(downDot);
        model.Series.Add(upDot);
        foreach (Axis axis in new Axis[] { x, y })
        {
            axis.FontSize = 12;
            axis.FontWeight = 400;
            axis.MajorGridlineStyle = axis == x ? LineStyle.None : LineStyle.Solid;
            axis.MinorGridlineStyle = LineStyle.None;
            axis.TickStyle = TickStyle.None;
            axis.IsPanEnabled = axis.IsZoomEnabled = false;
            model.Axes.Add(axis);
        }
        model.PlotAreaBorderColor = OxyColors.Undefined;
        model.PlotMargins = new OxyThickness(58, 14, 14, 32);
        model.Padding = new OxyThickness(0);
        model.Annotations.Add(cursor);
        var controller = new PlotController();
        controller.UnbindAll(); // The stock click tracker is replaced by a combined hover card.
        plot = new PlotView { Model = model, Controller = controller, Background = Ui.Panel, Height = 184 };
        var graph = new Grid();
        graph.Children.Add(plot);
        empty.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        empty.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        empty.TextAlignment = TextAlignment.Center;
        empty.Margin = new Thickness(22);
        graph.Children.Add(empty);
        graph.Children.Add(overlay);
        pointerDown.FontWeight = pointerUp.FontWeight = System.Windows.FontWeights.SemiBold;
        var pointerText = new StackPanel();
        pointerText.Children.Add(pointerTime);
        pointerText.Children.Add(pointerDown);
        pointerText.Children.Add(pointerUp);
        pointer = new Border { Child = pointerText, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed };
        overlay.Children.Add(pointer);
        Children.Add(graph);
        Children.Add(pan);
        var legend = new WrapPanel { Margin = new Thickness(0, 3, 0, 5) };
        var d = Ui.Label("↓ 下载", 12, Ui.Down);
        d.Margin = new Thickness(0, 0, 20, 0);
        legend.Children.Add(d);
        legend.Children.Add(Ui.Label("↑ 上传", 12, Ui.Up));
        Children.Add(legend);
        var interval = new DockPanel { Margin = new Thickness(0, 3, 0, 5) };
        clear = Ui.Button("清除区间", (_, _) => { selectedFirst = selectedLast = null; UpdateSelection(); });
        clear.Padding = new Thickness(9, 5, 9, 5);
        clear.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(clear, Dock.Right);
        interval.Children.Add(clear);
        interval.Children.Add(selection);
        Children.Add(interval);
        Children.Add(note);
        plot.PreviewMouseMove += (_, e) => { long now = Environment.TickCount64; if (now - hoverAt < 33) return; hoverAt = now; Hover(e.GetPosition(plot)); };
        plot.MouseLeave += (_, _) => HidePointer();
        plot.SizeChanged += (_, _) => { HidePointer(); model.InvalidatePlot(false); };
        plot.PreviewMouseWheel += (_, e) => { if (data.Count == 0) return; var p = e.GetPosition(plot); double ratio = (p.X - model.PlotArea.Left) / Math.Max(1, model.PlotArea.Width); Zoom(e.Delta > 0 ? 0.65 : 1 / 0.65, Math.Clamp(ratio, 0, 1)); e.Handled = true; };
        plot.PreviewMouseLeftButtonDown += (_, e) => { var p = e.GetPosition(plot); var area = model.PlotArea; if (p.X >= area.Left && p.X <= area.Right && p.Y >= area.Bottom - 5 && p.Y <= area.Bottom + 34) { Pick((long)Math.Round(FromAxis(x.InverseTransform(p.X)))); e.Handled = true; } };
        pan.ValueChanged += (_, _) => { if (syncing || full) return; long span = viewLast - viewFirst; viewFirst = (long)Math.Round(pan.Value); viewLast = viewFirst + span; Refresh(true); };
        period.SelectionChanged += (_, _) => { if (syncing) return; int index = Math.Max(0, period.SelectedIndex); app.Config.TimelineRange = index < 2 ? 0 : index - 1; if (index < 2) app.Config.TimelineRecentMinutes = index == 0 ? 10 : 60; ResetView(); changed(); Refresh(true); };
        metric.SelectionChanged += (_, _) => { if (syncing) return; int index = Math.Max(0, metric.SelectedIndex); app.Config.TimelineMetric = index == 2 ? 1 : 0; if (index < 2) app.Config.TimelinePeaks = index == 0; changed(); Refresh(true); };
        target.SelectionChanged += (_, _) => { if (!syncing) SelectTarget(target.SelectedIndex == 0 ? "" : target.SelectedItem as string ?? ""); };
        target.LostKeyboardFocus += (_, _) => { if (!syncing) CommitTarget(); };
        target.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitTarget(); e.Handled = true; } };
        SyncChoices();
        ApplyTheme();
    }
    void CommitTarget() => SelectTarget(target.Text == Strings.T("全部代理流量") ? "" : target.Text.Trim().TrimEnd('.').ToLowerInvariant());
    void ResetView()
    {
        full = true;
        selectedFirst = selectedLast = null;
    }
    public void SelectTarget(string value)
    {
        if (selectedTarget != value)
            selectedFirst = selectedLast = null;
        selectedTarget = value;
        syncing = true;
        target.Text = value.Length == 0 ? Strings.T("全部代理流量") : value;
        syncing = false;
        Refresh(true);
    }
    void SyncChoices()
    {
        syncing = true;
        period.ItemsSource = new[] { "近 10 分钟", "近 1 小时", "今天", "近 24 小时" }.Select(Strings.T).ToArray();
        metric.ItemsSource = new[] { "速率峰值", "平均速率", "每分钟用量" }.Select(Strings.T).ToArray();
        period.SelectedIndex = app.Config.TimelineRange == 0 ? (app.Config.TimelineRecentMinutes == 10 ? 0 : 1) : app.Config.TimelineRange + 1;
        metric.SelectedIndex = app.Config.TimelineMetric == 1 ? 2 : app.Config.TimelinePeaks ? 0 : 1;
        syncing = false;
    }
    public void UpdateTargets(IEnumerable<DailyRow> rows)
    {
        if (target.IsKeyboardFocusWithin)
            return;
        syncing = true;
        var names = rows.Select(r => r.Target).Take(200).ToList();
        if (selectedTarget.Length > 0 && !names.Contains(selectedTarget))
            names.Add(selectedTarget);
        target.ItemsSource = new[] { Strings.T("全部代理流量") }.Concat(names).ToArray();
        target.SelectedItem = selectedTarget.Length == 0 ? Strings.T("全部代理流量") : selectedTarget;
        target.Text = selectedTarget.Length == 0 ? Strings.T("全部代理流量") : selectedTarget;
        syncing = false;
    }
    static double ToAxis(long stamp) => DateTimeAxis.ToDouble(DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime);
    double FromAxis(double value) => (DateTime.SpecifyKind(x.ConvertToDateTime(value), DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;
    bool PeakView => app.Config.TimelineMetric == 0 && app.Config.TimelinePeaks;
    string MetricName => app.Config.TimelineMetric == 1 ? "每分钟用量" : PeakView ? "速率峰值" : "平均速率";
    double Value(TimelinePoint point, bool download) => PeakView ? download ? point.PeakDownload : point.PeakUpload : (download ? point.Download : point.Upload) / Math.Max(0.001, app.Config.TimelineMetric == 0 ? point.Seconds : point.Minutes);
    double GraphTime(TimelinePoint point) => ToAxis(point.Timestamp) + Math.Min(step, point.Resolution == 60 ? 60 : step) / 2.0 / 86400;
    public void Refresh(bool force = false)
    {
        if (!force && DateTime.UtcNow - refreshed < TimeSpan.FromSeconds(5))
            return;
        SyncChoices();
        var previousMouse = hoverPoint;
        HidePointer(false);
        fullLast = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;
        fullFirst = app.Config.TimelineRange switch
        {
            1 => new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds(),
            2 => fullLast - 86400,
            _ => fullLast - app.Config.TimelineRecentMinutes * 60
        };
        if (full)
        {
            viewFirst = fullFirst;
            viewLast = fullLast;
        }
        else
        {
            long span = Math.Min(viewLast - viewFirst, fullLast - fullFirst);
            viewFirst = Math.Clamp(viewFirst, fullFirst, fullLast - span);
            viewLast = viewFirst + span;
        }
        int visiblePoints = Math.Clamp((int)(plot.ActualWidth / 2.5), 180, 360);
        step = ChartMath.Step(viewLast - viewFirst, app.Config.TimelineMetric == 1, visiblePoints);
        long first = viewFirst / step * step, last = (viewLast + step - 1) / step * step;
        data = app.Store.QueryTimeline(DateTimeOffset.FromUnixTimeSeconds(first), DateTimeOffset.FromUnixTimeSeconds(last), selectedTarget, step);
        double maximum = data.Count == 0 ? 0 : data.Max(p => Math.Max(Value(p, true), Value(p, false)));
        double scaleTop = Math.Max(detailScale ? 1024 : 1024 * 1024, maximum * 1.12);
        unit = scaleTop >= 1024 * 1024 ? 1024 * 1024 : 1024;
        suffix = (unit == 1024 ? "KB" : "MB") + (app.Config.TimelineMetric == 0 ? "/s" : "/min");
        unitLabel.Text = Strings.T(MetricName) + " · " + suffix;
        overview.Background = detailScale ? System.Windows.Media.Brushes.Transparent : Ui.Selected;
        detail.Background = detailScale ? Ui.Selected : System.Windows.Media.Brushes.Transparent;
        down.Points.Clear();
        up.Points.Clear();
        long? previous = null;
        int previousResolution = step;
        foreach (var p in data)
        {
            if (previous != null && p.Timestamp - previous > Math.Max(step, previousResolution))
            {
                down.Points.Add(DataPoint.Undefined);
                up.Points.Add(DataPoint.Undefined);
            }
            double time = GraphTime(p);
            down.Points.Add(new(time, Value(p, true) / unit));
            up.Points.Add(new(time, Value(p, false) / unit));
            previous = p.Timestamp;
            previousResolution = p.Resolution;
        }
        x.Minimum = ToAxis(viewFirst);
        x.Maximum = ToAxis(viewLast);
        x.StringFormat = viewLast - viewFirst <= 600 ? "HH:mm:ss" : "HH:mm";
        x.IntervalType = viewLast - viewFirst <= 600 ? DateTimeIntervalType.Seconds : DateTimeIntervalType.Auto;
        x.MajorStep = double.NaN;
        x.MinorStep = double.NaN;
        x.IntervalLength = 95;
        y.Maximum = Math.Ceiling(scaleTop / unit * 2) / 2;
        y.MajorStep = y.Maximum / 2;
        y.Title = null;
        y.StringFormat = "0.##";
        empty.Text = Strings.T(app.Config.RecordTimeline ? "等待时间记录\n采样保存后，曲线会出现在这里。" : "时间记录未开启\n开启后可查看流量随时间的变化。");
        empty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        note.Text = Strings.T(app.Config.RecordTimeline ? "逐秒记录 · 保留最近 24 小时" : "记录已暂停 · 已有历史仍可查看") + " · " + Strings.Format("显示粒度：{0} 秒", step) + "\n" + Strings.T("滚轮缩放 · 下方滑块平移 · 点击时间轴两处查看区间总量");
        if (data.Any(p => p.Resolution == 60))
            note.Text += "\n" + Strings.T("旧记录为分钟精度，放大后仍按原精度展示。");
        syncing = true;
        pan.Minimum = fullFirst;
        pan.Maximum = Math.Max(fullFirst, fullLast - (viewLast - viewFirst));
        pan.Value = viewFirst;
        pan.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        syncing = false;
        UpdateSelection(false);
        model.ResetAllAxes();
        model.InvalidatePlot(true);
        refreshed = DateTime.UtcNow;
        if (previousMouse is Point mouse)
            Hover(mouse);
        System.Windows.Automation.AutomationProperties.SetName(plot, Strings.T("上下行时间曲线"));
    }
    void Zoom(double factor, double ratio)
    {
        if (fullLast <= fullFirst)
            return;
        hoverPoint = null;
        long oldSpan = viewLast - viewFirst, span = Math.Clamp((long)Math.Round(oldSpan * factor), 10, fullLast - fullFirst);
        full = span == fullLast - fullFirst;
        viewFirst = Math.Clamp((long)Math.Round(viewFirst + oldSpan * ratio - span * ratio), fullFirst, fullLast - span);
        viewLast = viewFirst + span;
        Refresh(true);
    }
    void Pick(long stamp)
    {
        stamp = Math.Clamp(stamp, fullFirst, fullLast);
        if (selectedFirst == null || selectedLast != null)
        {
            selectedFirst = stamp;
            selectedLast = null;
        }
        else
        {
            selectedLast = stamp;
            if (selectedLast < selectedFirst)
                (selectedFirst, selectedLast) = (selectedLast, selectedFirst);
            if (selectedFirst == selectedLast)
                selectedLast = selectedFirst + 1;
        }
        UpdateSelection();
    }
    void UpdateSelection(bool invalidate = true)
    {
        model.Annotations.Remove(startLine);
        model.Annotations.Remove(endLine);
        model.Annotations.Remove(band);
        clear.Visibility = selectedFirst == null ? Visibility.Collapsed : Visibility.Visible;
        if (selectedFirst == null)
            selection.Text = Strings.T("点击时间轴，选择起点和终点。");
        else
        {
            startLine.X = ToAxis(selectedFirst.Value);
            model.Annotations.Add(startLine);
            string Format(long t) => DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("MM-dd HH:mm:ss");
            if (selectedLast == null)
                selection.Text = Strings.Format("起点 {0} · 再点一次选择终点", Format(selectedFirst.Value));
            else
            {
                endLine.X = ToAxis(selectedLast.Value);
                model.Annotations.Add(endLine);
                band.MinimumX = startLine.X;
                band.MaximumX = endLine.X;
                model.Annotations.Add(band);
                var total = app.Store.QueryTimelineTotals(selectedFirst.Value, selectedLast.Value, selectedTarget);
                selection.Text = $"{Format(selectedFirst.Value)} — {Format(selectedLast.Value)}\n↓ {Display.Bytes(total.Download)}    ↑ {Display.Bytes(total.Upload)}    " + Strings.Format("已监测 {0} / {1} 秒", total.Seconds.ToString("0.#"), (selectedLast.Value - selectedFirst.Value).ToString());
                if (total.PartialLegacyMinutes > 0)
                    selection.Text += "\n" + Strings.T("边界未覆盖完整的旧分钟记录不计入区间总量。");
            }
        }
        if (invalidate)
            model.InvalidatePlot(false);
    }
    void Hover(Point mouse)
    {
        var area = model.PlotArea;
        if (!area.Contains(mouse.X, mouse.Y) || data.Count == 0)
        {
            HidePointer();
            return;
        }
        double timestamp = FromAxis(x.InverseTransform(mouse.X));
        int low = 0, high = data.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (data[middle].Timestamp < timestamp)
                low = middle + 1;
            else
                high = middle;
        }
        int index = Math.Min(low, data.Count - 1);
        if (index > 0 && Math.Abs(data[index - 1].Timestamp - timestamp) < Math.Abs(data[index].Timestamp - timestamp))
            index--;
        var p = data[index];
        if (Math.Abs(p.Timestamp - timestamp) > Math.Max(step, p.Resolution))
        {
            HidePointer();
            return;
        }
        hoverPoint = mouse;
        cursor.X = GraphTime(p);
        downDot.Points.Clear();
        upDot.Points.Clear();
        downDot.Points.Add(new(cursor.X, Value(p, true) / unit));
        upDot.Points.Add(new(cursor.X, Value(p, false) / unit));
        string format = viewLast - viewFirst > 3600 ? "MM-dd HH:mm:ss" : "HH:mm:ss";
        pointerTime.Text = DateTimeOffset.FromUnixTimeSeconds(p.Timestamp).LocalDateTime.ToString(format) + (step > 1 ? " — " + DateTimeOffset.FromUnixTimeSeconds(p.Timestamp + step).LocalDateTime.ToString(format) : "");
        string Amount(bool download) => app.Config.TimelineMetric == 0 ? Display.Speed(Value(p, download)) : Display.Bytes((long)Math.Round(Value(p, download))) + "/min";
        pointerDown.Text = Strings.T("↓ 下载") + "  " + Amount(true);
        pointerUp.Text = Strings.T("↑ 上传") + "  " + Amount(false);
        pointer.Visibility = Visibility.Visible;
        double availableWidth = Math.Max(1, overlay.ActualWidth);
        pointer.MaxWidth = Math.Max(150, availableWidth - 12);
        pointer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = pointer.DesiredSize.Width, height = pointer.DesiredSize.Height;
        Canvas.SetLeft(pointer, Math.Clamp(mouse.X + 14, 6, Math.Max(6, availableWidth - width - 6)));
        Canvas.SetTop(pointer, Math.Clamp(mouse.Y - height - 12, 6, Math.Max(6, plot.ActualHeight - height - 35)));
        model.InvalidatePlot(false);
    }
    void HidePointer(bool invalidate = true)
    {
        hoverPoint = null;
        bool visible = pointer.Visibility == Visibility.Visible;
        pointer.Visibility = Visibility.Collapsed;
        cursor.X = double.NaN;
        downDot.Points.Clear();
        upDot.Points.Clear();
        if (visible && invalidate)
            model.InvalidatePlot(false);
    }
    public void ApplyTheme()
    {
        if (paletteVersion == AppTheme.Version)
            return;
        paletteVersion = AppTheme.Version;
        OxyColor C(string key) => OxyColor.FromUInt32(0xff000000 | AppTheme.Color(key));
        model.Background = OxyColors.Transparent;
        model.TextColor = C("Ink");
        down.Color = downDot.MarkerFill = C("Accent");
        up.Color = upDot.MarkerFill = C("Upload");
        downDot.MarkerStroke = upDot.MarkerStroke = C("Surface");
        foreach (var axis in model.Axes)
        {
            axis.TextColor = C("Muted");
            axis.AxislineColor = axis.MajorGridlineColor = C("Line");
        }
        cursor.Color = C("Muted");
        startLine.Color = endLine.Color = C("Accent");
        band.Fill = OxyColor.FromAColor(28, C("Accent"));
        model.InvalidatePlot(false);
    }
    public void VerifyForTest(string output)
    {
        var utc = DateTimeOffset.UtcNow;
        var shown = (DateTime)x.GetValue(ToAxis(utc.ToUnixTimeSeconds()));
        if (Math.Abs((shown - utc.LocalDateTime).TotalSeconds) > 1)
            throw new InvalidOperationException("Timeline local timezone failed.");
        target.ApplyTemplate();
        if (target.Template.FindName("PART_EditableTextBox", target) is not TextBox)
            throw new InvalidOperationException("Editable target missing.");
        SelectTarget("downloads.example");
        if (down.Points.Count == 0 || up.Points.Any(p => double.IsFinite(p.Y) && p.Y != 0))
            throw new InvalidOperationException("Target filter failed.");
        SelectTarget("");
        Zoom(0.1, 0.8);
        long before = viewFirst, after = viewLast;
        Pick(viewLast - 120);
        Pick(viewFirst + 10);
        if (selectedFirst > selectedLast || !selection.Text.Contains("↓") || !selection.Text.Contains("↑"))
            throw new InvalidOperationException("Interval totals missing.");
        for (int i = 0; i < 100; i++)
            Refresh(true);
        if (viewFirst != before || viewLast != after || !ReferenceEquals(plot.Model, model) || down.Points.Count > 2500 || model.Annotations.Count != 4)
            throw new InvalidOperationException("Refresh changed zoom or retained plot resources.");
        string mode = app.Config.ColorMode;
        foreach (string theme in new[] { "light", "dark" })
        {
            Ui.Theme(Window.GetWindow(this), theme);
            ApplyTheme();
        }
        if (selectedLast == null || selectedTarget != "" || viewFirst != before)
            throw new InvalidOperationException("Theme lost chart state.");
        Ui.Theme(Window.GetWindow(this), mode);
        ApplyTheme();
        UpdateLayout();
        if (data.Count > 0)
        {
            Hover(new Point(x.Transform(GraphTime(data[data.Count / 2])), model.PlotArea.Top + 50));
            if (pointer.Visibility != Visibility.Visible || !pointerDown.Text.Contains("↓") || !pointerUp.Text.Contains("↑"))
                throw new InvalidOperationException("Combined hover tracker failed.");
            Refresh(true);
            if (pointer.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Refresh hid an idle hover tracker.");
        }
        selectedFirst = selectedLast = null;
        full = true;
        Refresh(true);
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "timeline-ui-test.txt"), "PASS: local timestamps, editable/filter targets, zoom and interval preserved across 100 refreshes and theme changes, bounded points/annotations\n");
    }
    public void PreviewForTest()
    {
        full = true;
        Refresh(true);
        Zoom(0.8, 0.7);
        Pick(viewFirst + 20);
        Pick(viewLast - 20);
        UpdateLayout();
        if (data.Count > 0)
        {
            var p = data[data.Count / 2];
            Hover(new Point(x.Transform(GraphTime(p)), model.PlotArea.Top + 80));
        }
    }
    public void PreviewLowTrafficForTest(bool detailView)
    {
        selectedFirst = selectedLast = null;
        detailScale = detailView;
        full = true;
        SelectTarget("low.example");
        if (!detailView && y.Maximum * unit < 1024 * 1024)
            throw new InvalidOperationException("Low traffic filled the burst scale.");
    }
    public string LayoutForTest => $"plot={plot.ActualWidth:0.#}, overlay={overlay.ActualWidth:0.#}, area={model.PlotArea}, popupX={Canvas.GetLeft(pointer):0.#}, popupWidth={pointer.DesiredSize.Width:0.#}";
    public void Dispose()
    {
        plot.Model = null;
        down.Points.Clear();
        up.Points.Clear();
        downDot.Points.Clear();
        upDot.Points.Clear();
        model.Annotations.Clear();
        data.Clear();
    }
}

// DateTimeAxis reconstructs unspecified DateTime values. Mark them UTC before formatting local labels.
public sealed class LocalTimeAxis : DateTimeAxis
{
    public override object GetValue(double value) => DateTime.SpecifyKind(ConvertToDateTime(value), DateTimeKind.Utc).ToLocalTime();
    protected override string FormatValueOverride(double value) => ((DateTime)GetValue(value)).ToString(StringFormat ?? "HH:mm");
}
