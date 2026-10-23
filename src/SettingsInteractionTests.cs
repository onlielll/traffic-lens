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

    public async Task PrepareUpdatePanelForTest()
    {
        SelectPage(3);
        availableRelease = null;
        updateStatus!.Text = Strings.T(ReleaseCheck.FailureMessage(new System.Net.Http.HttpRequestException("", null, System.Net.HttpStatusCode.Forbidden)));
        await Task.Delay(260);
        var page = (SmoothScrollViewer)pages[3];
        page.StopMotion(); page.ScrollToEnd();
        await Task.Delay(60);
        if (releaseButton == null || releaseButton.Visibility != Visibility.Visible || !releaseButton.IsEnabled)
            throw new InvalidOperationException("The releases page must remain accessible when update checks fail.");
    }
    public async Task VerifyClearOrderingAsync(string output)
    {
        string signal = Path.Combine(app.DataDirectory, "clear.signal");
        if (File.Exists(signal)) File.Delete(signal);
        var locked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool announcedBeforeClear = false;
        var exec = typeof(TrafficStore).GetMethod("Exec", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var blocker = Task.Run(() =>
        {
            using var store = new TrafficStore(Path.Combine(app.DataDirectory, "traffic.sqlite"));
            exec.Invoke(store, new object?[] { "BEGIN IMMEDIATE;", null });
            locked.SetResult(true);
            try
            {
                System.Threading.Thread.Sleep(250);
                announcedBeforeClear = File.Exists(signal);
            }
            finally { exec.Invoke(store, new object?[] { "COMMIT;", null }); }
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.ClearStatistics();
        await blocker;
        if (announcedBeforeClear || !File.Exists(signal))
            throw new InvalidOperationException("Clear notification preceded the SQLite clear transaction.");
        File.WriteAllText(Path.Combine(output, "clear-order-test.txt"), "PASS: committed local clear before notifying the resident, verified under a real write lock\n");
    }

    public async System.Threading.Tasks.Task VerifyNavigationAsync(string output)
    {
        SelectPage(2);
        await System.Threading.Tasks.Task.Delay(300);
        SelectPage(0);
        await System.Threading.Tasks.Task.Delay(300);
        double start = navigationPosition.Y;
        SelectPage(2);
        await System.Threading.Tasks.Task.Delay(70);
        double middle = navigationPosition.Y;
        await System.Threading.Tasks.Task.Delay(230);
        double end = navigationPosition.Y;
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "navigation-test.txt"), $"start={start:0.###}, middle={middle:0.###}, end={end:0.###}, systemAnimation={SystemParameters.ClientAreaAnimation}\n");
        if (SystemParameters.ClientAreaAnimation && (middle <= start || middle >= end || Math.Abs(end - 100) > 0.1))
            throw new InvalidOperationException("Navigation did not slide to the selected page.");
        for (int i = 0; i < 80; i++)
            SelectPage(i % 4);
        await System.Threading.Tasks.Task.Delay(260);
        if (pages.Count(p => p.Visibility == Visibility.Visible) != 1 || pages.Any(p => p.HasAnimatedProperties) || navigationPosition.HasAnimatedProperties)
            throw new InvalidOperationException("Navigation retained transition clocks.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "navigation-test.txt"), $"PASS: start={start:0.#}, middle={middle:0.#}, end={end:0.#}; rapid navigation ends with one page and no live animation clocks\n");
        SelectPage(0);
        await System.Threading.Tasks.Task.Delay(260);
    }
    public async System.Threading.Tasks.Task VerifyStatisticsForTest(string output)
    {
        SelectPage(2);
        SelectStatisticsView(0, false);
        await System.Threading.Tasks.Task.Delay(260);
        if (rankingView.Visibility != Visibility.Visible || timelineView.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Usage and timeline views overlap.");
        if (rows.Items.Count == 0)
            throw new InvalidOperationException("Usage examples missing.");
        var clickedRow = (ListViewItem)rows.ItemContainerGenerator.ContainerFromIndex(0)!;
        rows.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = PreviewMouseLeftButtonUpEvent,
            Source = clickedRow
        });
        await System.Threading.Tasks.Task.Delay(220);
        if (rankingView.Visibility != Visibility.Collapsed || timelineView.Visibility != Visibility.Visible || timeline!.SelectedTargetForTest != ((UsageRow)rows.Items[0]).Target)
            throw new InvalidOperationException("Target drill-in failed.");
        SelectStatisticsView(0, false);
        rows.SelectedIndex = 0;
        RefreshRows();
        if (rows.SelectedItem is not UsageRow selectedRow)
            throw new InvalidOperationException("Refresh lost keyboard selection.");
        rows.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rows), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });
        if (statisticsView != 1 || timeline!.SelectedTargetForTest != selectedRow.Target)
            throw new InvalidOperationException("Keyboard target drill-in failed.");
        await timeline.VerifyWheelForTest(output);
        for (int i = 0; i < 100; i++)
            SelectStatisticsView(i % 2, false);
        SelectStatisticsView(0, false);
        await System.Threading.Tasks.Task.Delay(80);
        if (rankingView.Visibility != Visibility.Visible || timelineView.Visibility != Visibility.Collapsed || statisticsPosition.HasAnimatedProperties)
            throw new InvalidOperationException("Repeated statistics switching retained transitions.");
        var card = (Border)((Grid)rows.Parent).Parent;
        if (card.Clip is not RectangleGeometry clip || clip.RadiusX != 16 || clip.Rect.Size != card.RenderSize || card.Padding.Left < 6)
            throw new InvalidOperationException("Table card does not clip and inset its content.");
        var container = (ListViewItem)rows.ItemContainerGenerator.ContainerFromIndex(0)!;
        container.ApplyTemplate();
        if (container.Template.FindName("Row", container) is not Border row || row.CornerRadius.TopLeft < 8 || row.Margin.Left < 4)
            throw new InvalidOperationException("Table row highlight is not rounded.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "statistics-ui-test.txt"), "PASS: usage view first, single visible view, routed mouse/Enter target drill-in, keyboard selection survives refresh, 100 switches without retained clocks, clipped/inset card and rounded rows\n");
    }

    public void PrepareQuotaForTest()
    {
        const long GB = 1_000_000_000;
        for (int i = 1; i <= 7; i++) app.Store.Add(new[] { new Increment(DateTime.Today.AddDays(-i), "downloads.example", GB * 3 / 4, GB / 4) });
        app.Config.QuotaGB = 48;
        app.Config.QuotaBaselineBytes = app.Store.ObservedBytes();
        app.Config.QuotaUpdatedUtc = DateTime.UtcNow;
        app.Config.ShowQuotaForecast = true;
        app.Save();
        quota!.Refresh(true);
    }

    public async Task VerifyReuseForTest(string output)
    {
        SelectPage(2); SelectStatisticsView(0, false); RefreshRows();
        await (rowsReadTask ?? Task.CompletedTask);
        var entries = rows.ItemsSource;
        RefreshRows();
        await (rowsReadTask ?? Task.CompletedTask);
        if (!ReferenceEquals(entries, rows.ItemsSource)) throw new InvalidOperationException("Unchanged ranking rebuilt its containers.");
        var theme = Resources.MergedDictionaries.Single();
        for (int i = 0; i < 100; i++) SetTheme(i % 2 == 0 ? "light" : "dark");
        if (!ReferenceEquals(theme, Resources.MergedDictionaries.Single())) throw new InvalidOperationException("Theme switch rebuilt styles.");
        quota!.VerifyCommitForTest();
        quota.Refresh(true);
        if (quota.DurationForTest != Strings.Format("约 {0:0} 天", 48)) throw new InvalidOperationException("Quota estimate was not shown.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "reuse-quota-test.txt"), "PASS: ranking source reused, 100 theme switches reuse styles, quota input/validation/subtraction and recorded-days forecast\n");
    }

    public void ShowQuotaForTest()
    {
        SelectPage(2); SelectStatisticsView(0, false);
        quota!.BringIntoView();
    }

    public void OpenAppearanceForTest() => appearance?.OpenForTest();
    public void CloseAppearanceForTest() => appearance?.CloseForTest();
    public UIElement? AppearancePreviewForTest => appearance?.PreviewForTest;
    public async Task VerifyTimelineForTest(string output) { if (timeline != null) await timeline.VerifyForTest(output); }
    public void PreviewTimelineForTest() => timeline?.PreviewForTest();
    public async Task PreviewLowTrafficForTest(bool detail) { if (timeline != null) await timeline.PreviewLowTrafficForTest(detail); }
    public string TimelineLayoutForTest => timeline?.LayoutForTest ?? "";

    public async System.Threading.Tasks.Task CheckToggleAnimationAsync(string output)
    {
        SelectPage(0);
        var toggle = checks[0].Box;
        toggle.ApplyTemplate();
        var knob = (TranslateTransform)toggle.Template.FindName("KnobPosition", toggle);
        bool original = toggle.IsChecked == true;
        await System.Threading.Tasks.Task.Delay(220);
        double start = knob.X;
        toggle.IsChecked = !original;
        await System.Threading.Tasks.Task.Delay(70);
        double middle = knob.X;
        await System.Threading.Tasks.Task.Delay(180);
        double end = knob.X;
        toggle.IsChecked = original;
        await System.Threading.Tasks.Task.Delay(200);
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "toggle-animation.txt"),
            $"PASS: start={start:0.###}, mid={middle:0.###}, end={end:0.###}; 160 ms transition\n");
        if (middle <= 0 || middle >= 16 || Math.Abs(start - end) < 15)
            throw new InvalidOperationException("Toggle animation did not interpolate between endpoints.");
    }
}
