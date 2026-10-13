using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace ProxyTrafficFloat;

/// <summary>A reusable appearance popover. No system menu gutter or persistent animation timer.</summary>
public sealed class AppearancePicker : Grid, IDisposable
{
    readonly Func<string> current;
    readonly Action<string> choose;
    readonly Popup popup;
    readonly TranslateTransform selectionPosition = new();
    readonly TextBlock title = Ui.Label("外观", 13);
    readonly TextBlock[] labels = new TextBlock[3];
    readonly string[] modes = { "system", "light", "dark" };
    readonly string[] names = { "跟随系统", "浅色", "深色" };
    readonly Button launcher;

    public AppearancePicker(Func<string> current, Action<string> choose)
    {
        this.current = current;
        this.choose = choose;
        launcher = new Button { Content = Ui.Icon("appearance", 18), Width = 34, Height = 32, Padding = new Thickness(7), Background = Brushes.Transparent };
        WindowChrome.SetIsHitTestVisibleInChrome(launcher, true);
        System.Windows.Automation.AutomationProperties.SetName(launcher, Strings.T("切换主题"));
        Children.Add(launcher);
        var content = new StackPanel();
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(3, 1, 0, 12);
        content.Children.Add(title);
        var options = new Grid { Height = 80 };
        for (int i = 0; i < 3; i++)
            options.ColumnDefinitions.Add(new ColumnDefinition());
        var track = new Border { Height = 76, Width = 86, Background = Ui.Selected, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = selectionPosition };
        options.Children.Add(track);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var tile = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = Ui.Icon(i == 0 ? "system" : i == 1 ? "sun" : "moon", 22, Ui.Text);
            icon.Margin = new Thickness(0, 0, 0, 7);
            tile.Children.Add(icon);
            labels[i] = Ui.Label(names[i], 11.5);
            labels[i].TextAlignment = TextAlignment.Center;
            tile.Children.Add(labels[i]);
            var button = new Button { Content = tile, Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0) };
            button.Click += (_, _) => { choose(modes[index]); Refresh(true); };
            Grid.SetColumn(button, i);
            options.Children.Add(button);
        }
        content.Children.Add(options);
        var shell = new Border { Child = content, Width = 286, Padding = new Thickness(12), Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(10), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.16, Color = Colors.Black } };
        popup = new Popup { Child = shell, AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = launcher, HorizontalOffset = -250, VerticalOffset = 3 };
        Children.Add(popup);
        launcher.Click += (_, _) => { Refresh(false); popup.IsOpen = !popup.IsOpen; };
        popup.Closed += (_, _) => { shell.BeginAnimation(OpacityProperty, null); selectionPosition.BeginAnimation(TranslateTransform.XProperty, null); };
        popup.Opened += (_, _) => { shell.Opacity = 0; shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop }); shell.Opacity = 1; };
    }
    public void Refresh(bool animate = false)
    {
        title.Text = Strings.T("外观");
        launcher.ToolTip = Strings.T("切换主题");
        for (int i = 0; i < 3; i++)
            labels[i].Text = Strings.T(names[i]);
        int index = Math.Max(0, Array.IndexOf(modes, current()));
        double before = selectionPosition.X, next = index * 86;
        selectionPosition.BeginAnimation(TranslateTransform.XProperty, null);
        selectionPosition.X = next;
        if (animate)
            selectionPosition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(before, next, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    public void OpenForTest()
    {
        Refresh();
        popup.IsOpen = true;
    }
    public void CloseForTest() => popup.IsOpen = false;
    public UIElement PreviewForTest => popup.Child;
    public void Dispose()
    {
        popup.IsOpen = false;
        selectionPosition.BeginAnimation(TranslateTransform.XProperty, null);
        popup.Child = null;
    }
}
