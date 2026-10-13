using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ProxyTrafficFloat;

// Settings only: animate real scroll offsets, keeping layout, hit testing and virtualization intact.
public sealed class SmoothScrollViewer : ScrollViewer
{
    readonly Motion page;
    Motion? nested;

    public SmoothScrollViewer()
    {
        page = new Motion(this);
        Unloaded += (_, _) => StopMotion();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopMotion(); };
    }

    public bool IsMoving => page.HasAnimatedProperties || (nested?.HasAnimatedProperties ?? false);

    public void StopMotion()
    {
        page.Stop();
        nested?.Stop();
        nested = null;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e) => Wheel(e);
    protected override void OnMouseWheel(MouseWheelEventArgs e) => Wheel(e);
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        StopMotion(); // Scrollbar dragging and track clicks take over immediately.
        base.OnPreviewMouseDown(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        StopMotion();
        base.OnPreviewKeyDown(e);
    }

    void Wheel(MouseWheelEventArgs e)
    {
        // Leave Ctrl+wheel to the chart's zoom handler.
        if (e.Handled || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
        int lines = SystemParameters.WheelScrollLines;
        double distance = -e.Delta / 120.0 * (lines < 0 ? ViewportHeight : lines * 24);
        double remaining = page.Move(distance);
        if (Math.Abs(remaining) > 0.01)
        {
            // Page first, then the table at the page boundary. The pointer needn't move.
            for (DependencyObject? source = e.OriginalSource as DependencyObject; source != null && source != this; source = AncestorOf(source))
            {
                if (source is not ScrollViewer child) continue;
                if (nested?.Viewer != child)
                {
                    nested?.Stop();
                    nested = new Motion(child);
                }
                nested.Move(remaining);
                break;
            }
        }
        e.Handled = true;
    }

    static DependencyObject? AncestorOf(DependencyObject source) => source is Visual
        ? VisualTreeHelper.GetParent(source)
        : (source as FrameworkContentElement)?.Parent;

    sealed class Motion : Animatable
    {
        static readonly DependencyProperty OffsetProperty = DependencyProperty.Register("Offset", typeof(double), typeof(Motion),
            new PropertyMetadata(0.0, (d, e) => ((Motion)d).Viewer.ScrollToVerticalOffset((double)e.NewValue)));
        public ScrollViewer Viewer { get; }
        double destination;
        int version;
        public Motion(ScrollViewer viewer) => Viewer = viewer;
        protected override Freezable CreateInstanceCore() => new Motion(Viewer);

        public double Move(double distance)
        {
            double start = HasAnimatedProperties ? destination : Viewer.VerticalOffset;
            double end = Math.Clamp(start + distance, 0, Viewer.ScrollableHeight);
            double remaining = distance - (end - start);
            if (Math.Abs(end - start) < 0.01) return remaining;
            int current = ++version;
            destination = end;
            double actual = Viewer.VerticalOffset;
            // Set the final base value before replacing the clock, so completion cannot jump back.
            SetValue(OffsetProperty, end);
            if (!SystemParameters.ClientAreaAnimation)
            {
                BeginAnimation(OffsetProperty, null);
                Viewer.ScrollToVerticalOffset(end);
                return remaining;
            }
            var animation = new DoubleAnimation(actual, end, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (_, _) => { if (version == current) BeginAnimation(OffsetProperty, null); };
            BeginAnimation(OffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
            return remaining;
        }

        public void Stop()
        {
            ++version;
            destination = Viewer.VerticalOffset;
            SetValue(OffsetProperty, destination);
            BeginAnimation(OffsetProperty, null);
        }
    }
}