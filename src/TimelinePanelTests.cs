using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace ProxyTrafficFloat;

public sealed partial class TimelinePanel
{
    public async Task VerifyForTest(string output)
    {
        var utc = DateTimeOffset.UtcNow;
        var shown = (DateTime)x.GetValue(ToAxis(utc.ToUnixTimeSeconds()));
        if (Math.Abs((shown - utc.LocalDateTime).TotalSeconds) > 1)
            throw new InvalidOperationException("Timeline local timezone failed.");
        target.ApplyTemplate();
        if (target.Template.FindName("PART_EditableTextBox", target) is not TextBox)
            throw new InvalidOperationException("Editable target missing.");
        SelectTarget("downloads.example");
        await PendingRead;
        if (down.Points.Count == 0 || up.Points.Any(p => double.IsFinite(p.Y) && p.Y != 0))
            throw new InvalidOperationException("Target filter failed.");
        SelectTarget("");
        Zoom(0.1, 0.8);
        await PendingRead;
        long before = viewFirst, after = viewLast;
        Pick(viewLast - 120);
        Pick(viewFirst + 10);
        await PendingRead;
        if (selectedFirst > selectedLast || !selection.Text.Contains("↓") || !selection.Text.Contains("↑"))
            throw new InvalidOperationException("Interval totals missing.");
        for (int i = 0; i < 100; i++)
            Refresh(true);
        await PendingRead;
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
            await PendingRead;
            if (pointer.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Refresh hid an idle hover tracker.");
        }
        selectedFirst = selectedLast = null;
        full = true;
        Refresh(true);
        await PendingRead;
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "timeline-ui-test.txt"), "PASS: local timestamps, editable/filter targets, zoom and interval preserved across 100 refreshes and theme changes, bounded points/annotations\n");
    }
    public async System.Threading.Tasks.Task VerifyWheelForTest(string output)
    {
        SelectTarget("");
        full = true;
        Refresh(true);
        await PendingRead;
        long before = viewLast - viewFirst;
        var point = new Point(model.PlotArea.Left + model.PlotArea.Width / 2, model.PlotArea.Top + 30);
        if (HandleWheel(120, ModifierKeys.None, point) || viewLast - viewFirst != before)
            throw new InvalidOperationException("Ordinary wheel changed the chart range.");
        if (!HandleWheel(120, ModifierKeys.Control, point) || viewLast - viewFirst >= before)
            throw new InvalidOperationException("Ctrl+wheel did not zoom the chart.");
        var element = System.Windows.Media.VisualTreeHelper.GetParent(plot);
        while (element != null && element is not ScrollViewer)
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        var scroll = (ScrollViewer)element!;
        scroll.ScrollToTop();
        await System.Threading.Tasks.Task.Delay(60);
        before = viewLast - viewFirst;
        plot.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = plot
        });
        await System.Threading.Tasks.Task.Delay(60);
        if (scroll.ScrollableHeight > 0 && scroll.VerticalOffset == 0)
            throw new InvalidOperationException("PlotView swallowed normal page scrolling.");
        if (viewLast - viewFirst != before)
            throw new InvalidOperationException("Bubbling wheel zoomed the chart.");
        full = true;
        Refresh(true);
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "chart-wheel-test.txt"), "PASS: normal wheel scrolls the containing page without zoom; Ctrl+wheel zooms; plot resources reused\n");
    }

    public string SelectedTargetForTest => selectedTarget;
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
    public async Task PreviewLowTrafficForTest(bool detailView)
    {
        selectedFirst = selectedLast = null;
        detailScale = detailView;
        full = true;
        SelectTarget("low.example");
        await PendingRead;
        if (!detailView && y.Maximum * unit < 1024 * 1024)
            throw new InvalidOperationException("Low traffic filled the burst scale.");

    }
    public string LayoutForTest => $"plot={plot.ActualWidth:0.#}, overlay={overlay.ActualWidth:0.#}, area={model.PlotArea}, popupX={Canvas.GetLeft(pointer):0.#}, popupWidth={pointer.DesiredSize.Width:0.#}";
}
