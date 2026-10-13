using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProxyTrafficFloat;

public sealed partial class SettingsWindow
{
    sealed class DragProbeThumb : Thumb { public void SetDragging(bool value) => IsDragging = value; }
    static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Descendant<T>(child) is T nested) return nested;
        }
        return null;
    }

    public void PreparePageCaptureForTest(int index)
    {
        SelectPage(index);
        var page = (SmoothScrollViewer)pages[index];
        page.StopMotion(); page.ScrollToTop();
        if (index == 2) { Descendant<ScrollViewer>(rows)?.ScrollToTop(); rows.SelectedIndex = -1; }
    }

    public async Task VerifyScrollingAndSlidersAsync(string output)
    {
        Width = 820; Height = 650;
        app.Store.Add(Enumerable.Range(0, 80).Select(i => new Increment(DateTime.Today, "row-" + i + ".example", i + 1, 1)));
        SelectPage(2); SelectStatisticsView(0, false); RefreshRows();
        var outer = (SmoothScrollViewer)pages[2];
        outer.StopMotion(); outer.ScrollToTop();
        await Task.Delay(100);
        var inner = Descendant<ScrollViewer>(rows)!;
        var source = (ListViewItem)rows.ItemContainerGenerator.ContainerFromIndex(0)!;
        source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        await Task.Delay(50);
        double middle = outer.VerticalOffset;
        await Task.Delay(230);
        double end = outer.VerticalOffset;
        if (end <= 0 || inner.VerticalOffset != 0 || (SystemParameters.ClientAreaAnimation && (middle <= 0 || middle >= end)))
            throw new InvalidOperationException($"Table wheel did not smoothly scroll the page first: {middle}/{end}, inner={inner.VerticalOffset}.");
        for (int i = 0; i < 30; i++) source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        await Task.Delay(260);
        if (Math.Abs(outer.VerticalOffset - outer.ScrollableHeight) > 1 || inner.VerticalOffset == 0 || outer.IsMoving)
            throw new InvalidOperationException("Wheel didn't continue into the log at the page boundary, or retained clocks.");
        double pageEnd = outer.VerticalOffset;
        source = Descendant<ListViewItem>(rows)!;
        source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 30) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        await Task.Delay(260);
        if (outer.VerticalOffset >= pageEnd || Math.Abs(pageEnd - outer.VerticalOffset) > 30) throw new InvalidOperationException("High-resolution reverse wheel was lost or overstepped.");
        for (int i = 0; i < 100; i++)
        {
            outer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, i % 2 == 0 ? 30 : -30) { RoutedEvent = Mouse.MouseWheelEvent });
        }
        await Task.Delay(260);
        if (outer.IsMoving) throw new InvalidOperationException("Rapid scrolling retained animation clocks.");
        outer.StopMotion(); outer.ScrollToTop();
        await Task.Delay(80);
        await appearance!.VerifyForTest(output);
        SelectPage(0); await Task.Delay(260);
        var slider = Descendant<Slider>(pages[0])!;
        slider.BringIntoView(); await Task.Delay(100); UpdateLayout(); slider.ApplyTemplate();
        double original = slider.Value;
        slider.Value = 0.973;
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var thumb = track.Thumb; thumb.ApplyTemplate();
        thumb.RaiseEvent(new DragDeltaEventArgs(1.5, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        double dragged = slider.Value;
        if (dragged <= 0.973 || dragged >= 0.983 || Math.Abs(dragged * 100 - Math.Round(dragged * 100)) < 0.00001)
            throw new InvalidOperationException("Dragging snapped instead of moving continuously.");
        System.Windows.Controls.Slider.IncreaseSmall.Execute(null, slider);
        if (Math.Abs(slider.Value - dragged - 0.01) > 0.000001 || !slider.IsMoveToPointEnabled) throw new InvalidOperationException("Slider keyboard step or track interaction failed.");
        slider.Value = original;
        var originalThumb = thumb;
        var probe = new DragProbeThumb { Style = thumb.Style };
        track.Thumb = probe; UpdateLayout(); probe.ApplyTemplate(); thumb = probe;
        ScaleTransform KnobScale() => (ScaleTransform)((FrameworkElement)thumb.Template.FindName("Cap", thumb)).RenderTransform;
        // A newly attached thumb must finish loading before its native visual states are sampled.
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        if (!probe.IsLoaded) throw new InvalidOperationException("Slider probe did not load.");
        probe.SetDragging(true);
        bool interpolated = false;
        double pressedMiddle = 1;
        var pressTimer = System.Diagnostics.Stopwatch.StartNew();
        while (pressTimer.ElapsedMilliseconds < 750)
        {
            await Task.Delay(16);
            double value = KnobScale().ScaleX;
            if (value > 0.922 && value < 0.998) pressedMiddle = value;
            interpolated |= value > 0.922 && value < 0.998;
            if (Math.Abs(value - 0.92) < 0.001) break;
        }
        if (!thumb.IsDragging || Math.Abs(KnobScale().ScaleX - 0.92) > 0.001 || (SystemParameters.ClientAreaAnimation && !interpolated))
            throw new InvalidOperationException($"Native thumb press lacks feedback: dragging={thumb.IsDragging}, interpolated={interpolated}, end={KnobScale().ScaleX}.");
        SaveSlider("slider-dark-pressed.png");
        probe.SetDragging(false); await Task.Delay(180);
        for (int i = 0; i < 100; i++) { VisualStateManager.GoToState(thumb, "Pressed", false); VisualStateManager.GoToState(thumb, "Normal", false); }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        var idleScale = KnobScale();
        if (Math.Abs(idleScale.ScaleX - 1) > 0.001 || idleScale.HasAnimatedProperties)
            throw new InvalidOperationException($"Slider feedback retained clocks in idle state: value={idleScale.ScaleX}, animated={idleScale.HasAnimatedProperties}.");
        SaveSlider("slider-dark-normal.png");
        SetTheme("light"); await Task.Delay(200); SaveSlider("slider-light-normal.png"); SetTheme("dark");
        track.Thumb = originalThumb;
        File.WriteAllText(Path.Combine(output, "scroll-slider-test.txt"), $"PASS: page/table wheel handoff, eased offset {middle:0.#}->{end:0.#}, high-resolution deltas, 100 rapid wheels release clocks, continuous thumb drag {dragged:0.######}, 1% keyboard step, native press interpolation {pressedMiddle:0.###}->0.92, 100 feedback cycles idle with no clocks\n");
        void SaveSlider(string name)
        {
            var card = (FrameworkElement)((StackPanel)slider.Parent).Parent;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(card), null, new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
    }
}