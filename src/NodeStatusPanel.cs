using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ProxyTrafficFloat;

public sealed class NodeStatusPanel : StackPanel
{
    readonly MonitorApp app;
    readonly TextBlock caption = Ui.Label("暂无节点测速结果", 12);
    readonly TextBlock timestamp = Ui.Label("", 10, Ui.Muted);
    readonly Path spark = new() { Height = 34, Margin = new Thickness(0, 8, 0, 5), Stroke = Ui.Down, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    NodeLatency? shown;
    string language = "";
    bool fresh;
    public NodeStatusPanel(MonitorApp app)
    {
        this.app = app;
        caption.Tag = timestamp.Tag = null;
        Children.Add(caption); Children.Add(spark); Children.Add(timestamp);
        SizeChanged += (_, _) => Draw();
    }
    public void Refresh()
    {
        var next = app.Config.MonitorNodeLatency && app.Connected ? app.CurrentLatency : null;
        if (shown?.Node == next?.Node && shown?.At == next?.At && shown?.Delay == next?.Delay && shown?.Slow == next?.Slow && language == Strings.Language && fresh == (next?.Fresh ?? false)) return;
        shown = next; language = Strings.Language; fresh = next?.Fresh ?? false;
        caption.Text = shown?.Caption ?? Strings.T("暂无节点测速结果");
        timestamp.Text = shown == null ? Strings.T("等待 Clash 的测速结果或开启主动测速。") : Strings.Format("最近测速 {0} · 最近 {1} 次", shown.At.ToLocalTime().ToString("g"), shown.Samples.Length);
        Draw();
    }
    void Draw()
    {
        if (shown == null || shown.Samples.Length < 2) { spark.Data = null; return; }
        double maximum = 300, width = Math.Max(1, ActualWidth);
        foreach (int value in shown.Samples) maximum = Math.Max(maximum, value);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            bool started = false;
            for (int i = 0; i < shown.Samples.Length; i++)
            {
                if (shown.Samples[i] == 0) { started = false; continue; }
                var point = new Point(i * width / (shown.Samples.Length - 1), 32 - shown.Samples[i] * 30 / maximum);
                if (!started) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                started = true;
            }
        }
        geometry.Freeze(); spark.Data = geometry;
    }
}