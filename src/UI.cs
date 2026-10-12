using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ProxyTrafficFloat;

public static class Ui
{
    public static readonly FontFamily InterfaceFont = new("Segoe UI, Microsoft YaHei");
    public static readonly FontFamily NumberFont = new("Segoe UI");
    // Mutable shared brushes keep existing controls alive when the palette changes.
    static readonly Dictionary<string, SolidColorBrush> Palette = new();
    static readonly Dictionary<string, ThemeColor> ColorSlots = new();
    sealed class ThemeColor : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(Color), typeof(ThemeColor));
        public Color Value
        {
            get => (Color)GetValue(ValueProperty); set => SetValue(ValueProperty, value);
        }
    }
    public static Brush Background => PaletteBrush("Canvas");
    public static Brush Panel => PaletteBrush("Surface");
    public static Brush Line => PaletteBrush("Line");
    public static Brush Text => PaletteBrush("Ink");
    public static Brush Muted => PaletteBrush("Muted");
    public static Brush Down => PaletteBrush("Accent");
    public static Brush Up => PaletteBrush("Upload");
    public static Brush Selected => PaletteBrush("Selection");
    public static SolidColorBrush PaletteBrush(string key)
    {
        if (!Palette.TryGetValue(key, out var brush))
        {
            var color = new ThemeColor { Value = Color(key) };
            ColorSlots.Add(key, color);
            brush = new SolidColorBrush();
            // A color binding prevents WPF templates from freezing shared palette brushes.
            System.Windows.Data.BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new System.Windows.Data.Binding("Value") { Source = color });
            Palette.Add(key, brush);
        }
        return brush;
    }
    static Color Color(string key)
    {
        uint c = AppTheme.Color(key);
        return System.Windows.Media.Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    // Icons are original vector outlines, shared and frozen once per settings process.
    static readonly Dictionary<string, Geometry> Icons = new()
    {
        ["traffic"] = Geometry("M 5,2 L 5,18 M 1,14 L 5,18 L 9,14 M 15,18 L 15,2 M 11,6 L 15,2 L 19,6"),
        ["window"] = Geometry("M 2,4 L 18,4 L 18,17 L 2,17 Z M 2,8 L 18,8 M 5,6 L 6,6"),
        ["mini"] = Geometry("M 3,3 L 17,3 L 17,17 L 3,17 Z M 10,11 L 17,11 M 10,11 L 10,17"),
        ["chart"] = Geometry("M 2,3 L 2,18 L 18,18 M 6,14 L 6,10 M 11,14 L 11,6 M 16,14 L 16,3"),
        ["plug"] = Geometry("M 6,2 L 6,7 M 14,2 L 14,7 M 4,7 L 16,7 L 16,11 L 12,15 L 8,15 L 4,11 Z M 10,15 L 10,19"),
        ["close"] = Geometry("M 5,5 L 15,15 M 5,15 L 15,5"),
        ["appearance"] = Geometry("M 10,2 A 8,8 0 1 1 10,18 A 8,8 0 1 1 10,2 Z M 10,2 L 10,18"),
        ["system"] = Geometry("M 2,3 L 18,3 L 18,14 L 2,14 Z M 7,18 L 13,18 M 10,14 L 10,18"),
        ["sun"] = Geometry("M 14,10 A 4,4 0 1 1 6,10 A 4,4 0 1 1 14,10 M 10,0 L 10,2 M 10,18 L 10,20 M 0,10 L 2,10 M 18,10 L 20,10 M 3,3 L 4.5,4.5 M 15.5,15.5 L 17,17 M 3,17 L 4.5,15.5 M 15.5,4.5 L 17,3"),
        ["moon"] = Geometry("M 14,2 A 8,8 0 1 0 18,13 A 7,7 0 0 1 14,2 Z")
    };

    static Geometry Geometry(string path)
    {
        var geometry = System.Windows.Media.Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    public static Brush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    public static Viewbox Icon(string name, double size = 18, Brush? color = null)
    {
        var drawing = new Grid { Width = 20, Height = 20 };
        drawing.Children.Add(new Path
        {
            Data = Icons[name],
            Stroke = color ?? Muted,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        });
        return new Viewbox { Width = size, Height = size, Child = drawing };
    }

    public static TextBlock Label(string text, double size = 12, Brush? color = null) => new()
    {
        Text = Strings.T(text),
        Tag = text,
        FontSize = size < 15 ? size + 1 : size,
        FontFamily = InterfaceFont,
        Foreground = color ?? Text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };

    public static Button Button(string text, RoutedEventHandler action)
    {
        var button = new Button { Content = Strings.T(text), Tag = text, Margin = new Thickness(0, 3, 6, 3) };
        button.Click += action;
        return button;
    }

    public static void Theme(Window window, string mode, bool systemChanged = false)
    {
        bool changed = AppTheme.Apply(mode, systemChanged);
        if (!changed && window.Resources.MergedDictionaries.Count != 0)
            return;
        foreach (string key in AppTheme.Colors.Keys)
        {
            var brush = PaletteBrush(key);
            ColorSlots[key].Value = Color(key);
            window.Resources[key] = brush;
            Application.Current.Resources[key] = brush;
            window.Resources[key + "Color"] = brush.Color;
            Application.Current.Resources[key + "Color"] = brush.Color;
        }
        string assembly = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
        window.Resources.MergedDictionaries.Clear();
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/" + assembly + ";component/Theme.xaml", UriKind.Relative)
        });
    }

    public static Border Card(UIElement content, Thickness? padding = null) => new()
    {
        Background = Panel,
        CornerRadius = new CornerRadius(16),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Padding = padding ?? new Thickness(18),
        Margin = new Thickness(0, 0, 0, 12),
        Child = content
    };
}

public record DesktopScreen(string Name, int Left, int Top, int Right, int Bottom, double Scale)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

public static class DesktopScreens
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr arg);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr arg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref Info info);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    public static List<DesktopScreen> Get()
    {
        var screens = new List<DesktopScreen>();
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new Info { Size = Marshal.SizeOf<Info>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                double scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
                screens.Add(new(info.Device, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, scale));
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return screens;
    }

    public static DesktopScreen ForWindow(Window window)
    {
        var screens = Get();
        var handle = new WindowInteropHelper(window).Handle;
        if (GetWindowRect(handle, out var bounds))
        {
            return screens.OrderByDescending(screen =>
                Math.Max(0, Math.Min(screen.Right, bounds.Right) - Math.Max(screen.Left, bounds.Left)) *
                Math.Max(0, Math.Min(screen.Bottom, bounds.Bottom) - Math.Max(screen.Top, bounds.Top))).First();
        }
        return screens.First();
    }
}

public sealed class SettingsWindow : Window
{
    readonly MonitorApp app;
    readonly List<(CheckBox Box, Func<bool> Value)> checks = new();
    readonly List<FrameworkElement> pages = new();
    readonly List<Button> navigation = new();
    readonly List<TextBlock> navigationLabels = new();
    readonly List<(ComboBox Box, string[] Keys)> choices = new();
    readonly List<(FrameworkElement Element, string Name, string Help)> accessible = new();
    readonly TextBlock pageTitle = Ui.Label("", 26);
    readonly TextBlock pageNote = Ui.Label("", 11, Ui.Muted);
    readonly TextBlock connection = Ui.Label("", 12, Ui.Down);
    readonly TextBlock total = Ui.Label("", 25);
    readonly ListView rows = new() { Height = 250 };
    readonly TextBlock emptyRows = Ui.Label("还没有监测记录\n有代理流量时，记录会自动出现在这里。", 12, Ui.Muted);
    readonly ComboBox range = new() { Width = 116 };
    readonly ComboBox sort = new() { Width = 148, Margin = new Thickness(8, 0, 0, 0) };
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly string[] titles = { "悬浮窗", "智能收起", "流量记录", "连接与启动" };
    readonly string[] notes =
    {
        "把关注的信息留在桌面，把空间留给工作。",
        "小窗安静待命，速度超过阈值时展开。",
        "找到流量来源。记录只存储在这台电脑。",
        "连接本机 Mihomo，恢复你习惯的使用方式。"
    };

    HwndSource? source;
    bool syncing, pendingSave;
    ComboBox? languagePicker;
    int currentPage, transitionVersion;
    DateTime lastStats = DateTime.MinValue;
    TimelinePanel? timeline;
    AppearancePicker? appearance;
    readonly TranslateTransform navigationPosition = new();
    readonly Border navigationPill = new() { Height = 46, CornerRadius = new CornerRadius(12), Background = Ui.Selected, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };

    public SettingsWindow(MonitorApp app)
    {
        this.app = app;
        Ui.Theme(this, app.Config.ColorMode);
        Strings.UseLanguage(app.Config.Language);
        appearance?.Refresh();
        Title = Strings.T("Traffic Lens · 代理流量");
        Width = 820;
        Height = 650;
        MinWidth = 480;
        MinHeight = 360;
        Background = Ui.Background;
        Foreground = Ui.Text;
        FontFamily = Ui.InterfaceFont;
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 48,
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(16),
            GlassFrameThickness = new Thickness(0)
        });

        var deck = BuildShell();
        BuildAppearance(Page(deck));
        BuildSmart(Page(deck));
        BuildStatistics(Page(deck));
        BuildConnection(Page(deck));

        saveTimer.Tick += (_, _) => { saveTimer.Stop(); FlushSave(); };
        app.Updated += Refresh;
        Closed += (_, _) =>
        {
            saveTimer.Stop();
            FlushSave();
            app.Updated -= Refresh;
            timeline?.Dispose();
            appearance?.Dispose();
            source?.RemoveHook(Hook);
            source = null;
        };
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(Hook);
            FitScreen();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ApplyLanguage();
    }

    Grid BuildShell()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(48)
        });
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        Content = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = root };

        var caption = new DockPanel { Margin = new Thickness(20, 0, 10, 0) };
        var close = new Button
        {
            Content = Ui.Icon("close", 16),
            Width = 34,
            Height = 30,
            Background = Brushes.Transparent,
            Padding = new Thickness(7),
            ToolTip = Strings.T("关闭设置")
        };
        AccessibleName(close, "关闭设置");
        close.Click += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        appearance = new AppearancePicker(() => app.Config.ColorMode, SetTheme);
        DockPanel.SetDock(appearance, Dock.Right);
        caption.Children.Add(appearance);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Ui.Icon("traffic", 19, Ui.Down));
        var brandText = Ui.Label("TRAFFIC LENS", 11, Ui.Text);
        brandText.FontWeight = FontWeights.SemiBold;
        brandText.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(brandText);
        caption.Children.Add(brand);
        root.Children.Add(caption);

        var main = new Grid();
        var sidebarWidth = new ColumnDefinition { Width = new GridLength(174) };
        main.ColumnDefinitions.Add(sidebarWidth);
        main.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        Grid.SetRow(main, 1);
        root.Children.Add(main);

        var sidebar = new DockPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            LastChildFill = true
        };
        var footer = new StackPanel { Margin = new Thickness(14, 0, 8, 16) };
        footer.Children.Add(Ui.Label("LOCAL & PRIVATE", 9, Ui.Down));
        footer.Children.Add(Ui.Label("每秒更新 · 自动保存", 10, Ui.Muted));
        DockPanel.SetDock(footer, Dock.Bottom);
        sidebar.Children.Add(footer);
        var navigationPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var sidebarTitle = Ui.Label("偏好设置", 10, Ui.Muted);
        sidebarTitle.Margin = new Thickness(14, 0, 0, 13);
        navigationPanel.Children.Add(sidebarTitle);
        var navDeck = new Grid();
        navigationPill.RenderTransform = navigationPosition;
        navDeck.Children.Add(navigationPill);
        var navItems = new StackPanel();
        navDeck.Children.Add(navItems);
        navigationPanel.Children.Add(navDeck);
        string[] symbols = { "window", "mini", "chart", "plug" };
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Ui.Icon(symbols[i], 17));
            var label = Ui.Label(titles[i], 12);
            label.Margin = new Thickness(10, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            navigationLabels.Add(label);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource("NavigationButton"),
                Height = 46,
                Margin = new Thickness(0, 0, 0, 4),
                Background = Brushes.Transparent,
                Padding = new Thickness(13, 9, 8, 9),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = Strings.T(titles[i])
            };
            AccessibleName(button, titles[i]);
            button.Click += (_, _) => SelectPage(index);
            navigation.Add(button);
            navItems.Children.Add(button);
        }
        sidebar.Children.Add(navigationPanel);
        main.Children.Add(sidebar);

        var contentArea = new Grid { Margin = new Thickness(24, 16, 24, 18) };
        contentArea.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        contentArea.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        Grid.SetColumn(contentArea, 1);
        main.Children.Add(contentArea);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        pageTitle.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(pageTitle);
        heading.Children.Add(pageNote);
        contentArea.Children.Add(heading);
        var deck = new Grid { ClipToBounds = true };
        Grid.SetRow(deck, 1);
        contentArea.Children.Add(deck);

        // Collapse labels, not controls, on small screens; every page remains keyboard accessible.
        main.SizeChanged += (_, _) =>
        {
            bool narrow = main.ActualWidth < 640;
            sidebarWidth.Width = new GridLength(narrow ? 58 : 174);
            sidebarTitle.Visibility = footer.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            foreach (var label in navigationLabels)
                label.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            contentArea.Margin = new Thickness(narrow ? 14 : 24, 16, narrow ? 14 : 24, 18);
        };
        return deck;
    }

    StackPanel Section(string title, string? note = null)
    {
        var section = new StackPanel();
        var heading = Ui.Label(title, 15);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 8);
        section.Children.Add(heading);
        if (note != null)
            section.Children.Add(Ui.Label(note, 11, Ui.Muted));
        return section;
    }

    void BuildAppearance(StackPanel page)
    {
        Toggle(page, "显示悬浮窗", "隐藏后仍在后台监测，随时从托盘恢复。",
            () => app.Config.Visible, value => app.Config.Visible = value);

        var display = Section("信息展示");
        Toggle(display, "代理总速度", "当前全部代理连接的上传与下载速度。",
            () => app.Config.ShowSpeed, value => app.Config.ShowSpeed = value, false);
        Toggle(display, "下载最快目标", "域名、速度和所用节点。",
            () => app.Config.ShowDownload, value => app.Config.ShowDownload = value, false);
        Toggle(display, "上传最快目标", "与下载并排展示，分别计算第一名。",
            () => app.Config.ShowUpload, value => app.Config.ShowUpload = value, false);
        Toggle(display, "今日监测累计", "今天监测到的代理上传与下载总量。",
            () => app.Config.ShowTotal, value => app.Config.ShowTotal = value, false);
        Toggle(display, "最快目标累计", "额外显示第一名目标今天的流量。",
            () => app.Config.ShowTargetTotal, value => app.Config.ShowTargetTotal = value, false);
        page.Children.Add(Ui.Card(display));

        var layout = Section("大小与位置");
        var grouping = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        Choice(grouping, "按域名合并 · 无域名时按 IP", "按单条连接");
        grouping.SelectedIndex = app.Config.SingleConnection ? 1 : 0;
        AccessibleName(grouping, "流量排名方式");
        grouping.SelectionChanged += (_, _) =>
        {
            if (syncing)
                return;
            app.Config.SingleConnection = grouping.SelectedIndex == 1;
            app.Recalculate();
            Changed();
        };
        layout.Children.Add(grouping);
        Slider(layout, "界面大小", 0.8, 1.25, app.Config.UiScale, value => app.Config.UiScale = value,
            "同时适配 Windows 显示缩放。");
        Slider(layout, "透明度", 0.35, 1, app.Config.Opacity, value => app.Config.Opacity = value,
            "降低透明度，减少对桌面的遮挡。");
        page.Children.Add(Ui.Card(layout));

        var interaction = Section("窗口行为");
        Toggle(interaction, "边缘吸附", "详细窗和迷你窗都可拖动，接近边缘时吸附。",
            () => app.Config.SnapToEdges, value => app.Config.SnapToEdges = value, false);
        Toggle(interaction, "始终置顶", "关闭后可被其他应用窗口遮挡。",
            () => app.Config.AlwaysOnTop, value => app.Config.AlwaysOnTop = value, false);
        Toggle(interaction, "锁定交互", "锁定后，只有小锁按钮可以点击。",
            () => app.Config.Locked, value => app.Config.Locked = value, false);
        Toggle(interaction, "锁定后点击穿透", "除小锁按钮外，点击落到下方的应用。",
            () => app.Config.LockClickThrough, value => app.Config.LockClickThrough = value, false);
        Toggle(interaction, "鼠标穿透", "整个窗口穿透；可从托盘菜单解除。",
            () => app.Config.ClickThrough, value => app.Config.ClickThrough = value, false);
        page.Children.Add(Ui.Card(interaction));
    }

    void BuildSmart(StackPanel page)
    {
        Toggle(page, "迷你模式", "只保留最快目标和上下行速度；点击可临时展开。",
            () => app.Config.MiniMode, value => app.Config.MiniMode = value);

        var corner = Section("初始停靠位置", "从所选角落开始；拖动后记住位置。");
        var location = new ComboBox { Margin = new Thickness(0, 10, 0, 6) };
        Choice(location, "右下角", "左下角", "右上角", "左上角");
        location.SelectedIndex = app.Config.Corner;
        AccessibleName(location, "迷你模式初始角落");
        location.SelectionChanged += (_, _) => { if (syncing) return; app.Config.Corner = location.SelectedIndex; Changed(); };
        corner.Children.Add(location);
        Toggle(corner, "固定停靠角落", "关闭后可自由拖动迷你窗。",
            () => app.Config.MiniDockCorner, value => app.Config.MiniDockCorner = value, false);
        page.Children.Add(Ui.Card(corner));

        Toggle(page, "流量变大时自动展开", "任一最快目标超过阈值并持续一段时间后展开。",
            () => app.Config.AutoExpand, value => app.Config.AutoExpand = value);

        var thresholds = Section("展开与收起");
        Number(thresholds, "下载速度", app.Config.DownloadThresholdKB, "KB/s", 1, 1048576,
            value => app.Config.DownloadThresholdKB = value);
        Number(thresholds, "上传速度", app.Config.UploadThresholdKB, "KB/s", 1, 1048576,
            value => app.Config.UploadThresholdKB = value);
        Number(thresholds, "超速持续时间", app.Config.TriggerSeconds, "秒", 1, 30,
            value => app.Config.TriggerSeconds = value);
        Number(thresholds, "安静多久后收起", app.Config.QuietSeconds, "秒", 3, 120,
            value => app.Config.QuietSeconds = value);
        var note = Ui.Label("两者都低于各自阈值的一半时，开始计算收起时间。点击迷你窗后，详细界面至少保留 15 秒。", 11, Ui.Muted);
        note.Margin = new Thickness(0, 12, 0, 0);
        thresholds.Children.Add(note);
        page.Children.Add(Ui.Card(thresholds));
    }

    void BuildStatistics(StackPanel page)
    {
        var summary = Section("监测累计");
        total.Margin = new Thickness(0, 0, 0, 8);
        summary.Children.Add(total);
        summary.Children.Add(Ui.Label("保留最近 30 天 · 全部存储在本机", 11, Ui.Muted));
        page.Children.Add(Ui.Card(summary, new Thickness(18)));

        Choice(range, "今天", "近 7 天");
        range.SelectedIndex = 0;
        Choice(sort, "按总量排序", "按下载排序", "按上传排序");
        sort.SelectedIndex = 0;
        AccessibleName(range, "统计日期范围");
        AccessibleName(sort, "统计排序方式");
        range.SelectionChanged += (_, _) => { if (!syncing) RefreshRows(); };
        sort.SelectionChanged += (_, _) => { if (!syncing) RefreshRows(); };
        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        selectors.Children.Add(range);
        selectors.Children.Add(sort);

        var history = Section("时间线");
        Toggle(history, "记录流量时间线", "逐秒保存 · 最近 24 小时",
            () => app.Config.RecordTimeline, value => { app.Config.RecordTimeline = value; timeline?.Refresh(true); }, false);
        timeline = new TimelinePanel(app, Changed);
        history.Children.Add(timeline);
        page.Children.Add(Ui.Card(history));

        page.Children.Add(selectors);
        var columns = new GridView();
        columns.Columns.Add(new GridViewColumn { Header = Strings.T("目标"), Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding("Target") });
        columns.Columns.Add(new GridViewColumn { Header = Strings.T("下载"), Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Down") });
        columns.Columns.Add(new GridViewColumn { Header = Strings.T("上传"), Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Up") });
        rows.View = columns;
        AccessibleName(rows, "目标流量排行");
        rows.SizeChanged += (_, _) =>
        {
            var available = Math.Max(220, rows.ActualWidth - 22);
            columns.Columns[0].Width = available * 0.5;
            columns.Columns[1].Width = available * 0.25;
            columns.Columns[2].Width = available * 0.25;
        };
        var table = new Grid();
        table.Children.Add(rows);
        emptyRows.HorizontalAlignment = HorizontalAlignment.Center;
        emptyRows.VerticalAlignment = VerticalAlignment.Center;
        emptyRows.TextAlignment = TextAlignment.Center;
        table.Children.Add(emptyRows);
        page.Children.Add(Ui.Card(table, new Thickness(0)));
        var clear = Ui.Button("清空记录", (_, _) =>
        {
            if (MessageBox.Show(this, Strings.T("清空每日统计和全部时间线记录？此操作无法撤销。"), Strings.T("清空记录"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                app.ClearStatistics();
                RefreshRows();
            }
        });
        clear.Foreground = Ui.PaletteBrush("Danger");
        clear.Background = Ui.PaletteBrush("DangerBackground");
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(clear);
        page.Children.Add(Ui.Label("仅记录监测期间的有效增量。短连接可能漏记，断线期间不补算；用于定位来源，不作为精确账单。", 11, Ui.Muted));
    }

    void BuildConnection(StackPanel page)
    {
        var language = Section("界面语言", "自动选择 Windows 显示语言；切换立即生效。");
        var picker = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        languagePicker = picker;
        Choice(picker, "跟随系统", "简体中文", "English");
        string[] languages = { "auto", "zh-CN", "en" };
        picker.SelectedIndex = Array.IndexOf(languages, app.Config.Language);
        if (picker.SelectedIndex < 0)
            picker.SelectedIndex = 0;
        AccessibleName(picker, "语言");
        picker.SelectionChanged += (_, _) =>
        {
            if (syncing || picker.SelectedIndex < 0)
                return;
            SetLanguage(languages[picker.SelectedIndex]);
        };
        language.Children.Add(picker);
        page.Children.Add(Ui.Card(language));

        var state = Section("本地连接");
        state.Children.Add(connection);
        state.Children.Add(Ui.Label("内核在线表示接口可读取。速率采用最近三秒平均，停止传输后会短暂保留；关闭系统代理和 TUN 不会清空今日累计。", 11, Ui.Muted));
        state.Children.Add(Ui.Label("自动识别当前运行的 Mihomo 接口，通常无需手动配置。", 11, Ui.Muted));
        page.Children.Add(Ui.Card(state));

        var advanced = Section("手动连接", "地址留空即自动发现；支持本机 HTTP 或 Windows 命名管道。");
        advanced.Children.Add(Ui.Label("接口地址", 11, Ui.Muted));
        var endpoint = new TextBox { Text = app.Config.Endpoint, Margin = new Thickness(0, 6, 0, 12) };
        AccessibleName(endpoint, "接口地址");
        advanced.Children.Add(endpoint);
        advanced.Children.Add(Ui.Label("接口密钥", 11, Ui.Muted));
        var secret = new PasswordBox { Password = app.Config.Secret, Margin = new Thickness(0, 6, 0, 12) };
        AccessibleName(secret, "接口密钥");
        advanced.Children.Add(secret);
        var reconnect = Ui.Button("保存并重新连接", (_, _) =>
        {
            try
            {
                string address = endpoint.Text.Trim();
                if (address.Length > 0)
                    using (MihomoClient.CreateClient(address, secret.Password))
                    {
                    }
                app.Config.Endpoint = address;
                app.Config.Secret = secret.Password;
                app.Save();
                app.Reconnect();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Strings.T(ex.Message), Strings.T("连接设置"));
            }
        });
        reconnect.Style = (Style)FindResource("AccentButton");
        reconnect.HorizontalAlignment = HorizontalAlignment.Left;
        advanced.Children.Add(reconnect);
        page.Children.Add(Ui.Card(advanced));

        Toggle(page, "开机启动", "登录 Windows 后运行，恢复上次的展示状态。",
            () => app.Config.AutoStart, app.SetAutoStart);
        page.Children.Add(Ui.Label("密钥使用当前 Windows 用户加密保存。程序只读连接数据，不修改分流规则，也不进行外网测速。", 11, Ui.Muted));
        var about = Section("关于 Traffic Lens");
        about.Children.Add(Ui.Label("版本 2.1.0 · 仅在本机监测", 11, Ui.Muted));
        about.Children.Add(Ui.Label("Copyright © 2026 onlielll", 11, Ui.Muted));
        page.Children.Add(Ui.Card(about));
    }

    IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E or 0x001A)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { FitScreen(); Ui.Theme(this, app.Config.ColorMode, true); timeline?.ApplyTheme(); }));
        return IntPtr.Zero;
    }

    void FitScreen()
    {
        if (app.IsStopping)
            return;
        var screen = DesktopScreens.ForWindow(this);
        MaxWidth = Math.Max(300, screen.Width / screen.Scale - 24);
        MaxHeight = Math.Max(260, screen.Height / screen.Scale - 24);
        MinWidth = Math.Min(480, MaxWidth);
        MinHeight = Math.Min(360, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    StackPanel Page(Grid deck)
    {
        var panel = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = panel,
            RenderTransform = new TranslateTransform(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 4, 0)
        };
        pages.Add(scroll);
        deck.Children.Add(scroll);
        return panel;
    }

    void Choice(ComboBox box, params string[] keys)
    {
        choices.Add((box, keys));
        box.ItemsSource = keys.Select(Strings.T).ToArray();
    }

    void AccessibleName(FrameworkElement element, string name, string help = "")
    {
        accessible.Add((element, name, help));
        System.Windows.Automation.AutomationProperties.SetName(element, Strings.T(name));
        System.Windows.Automation.AutomationProperties.SetHelpText(element, Strings.T(help));
    }

    public void SetLanguage(string language)
    {
        FlushSave();
        Strings.UseLanguage(language);
        app.Config.Language = Strings.Language;
        app.Save();
        ApplyLanguage();
    }

    void ApplyLanguage()
    {
        Strings.UseLanguage(app.Config.Language);
        appearance?.Refresh();
        Title = Strings.T("Traffic Lens · 代理流量");
        FontFamily = Ui.InterfaceFont;
        // Retarget existing controls instead of rebuilding the window and retaining old event handlers.
        Translate(Content as DependencyObject);
        syncing = true;
        foreach (var (box, keys) in choices)
        {
            int selected = box.SelectedIndex;
            box.ItemsSource = keys.Select(Strings.T).ToArray();
            box.SelectedIndex = selected;
        }
        if (languagePicker != null)
            languagePicker.SelectedIndex = app.Config.Language == "en" ? 2 : app.Config.Language == "zh-CN" ? 1 : 0;
        syncing = false;
        foreach (var (element, name, help) in accessible)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, Strings.T(name));
            System.Windows.Automation.AutomationProperties.SetHelpText(element, Strings.T(help));
            if (element.ToolTip is string)
                element.ToolTip = Strings.T(name);
        }
        if (rows.View is GridView columns)
            for (int i = 0; i < columns.Columns.Count; i++)
                columns.Columns[i].Header = Strings.T(i == 0 ? "目标" : i == 1 ? "下载" : "上传");
        SelectPage(currentPage);
        Refresh();
        RefreshRows();
    }

    static void Translate(DependencyObject? element)
    {
        if (element == null)
            return;
        if (element is FrameworkElement view && view.Tag is string key && Strings.HasTranslation(key))
        {
            if (view is TextBlock text)
                text.Text = Strings.T(key);
            else if (view is Button button && button.Content is string)
                button.Content = Strings.T(key);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependency)
                Translate(dependency);
    }

    public void SelectPage(int index)
    {
        bool moved = index != currentPage;
        bool animate = IsLoaded && moved && SystemParameters.ClientAreaAnimation;
        int direction = index >= currentPage ? 1 : -1;
        currentPage = index;
        double start = navigationPosition.Y, end = index * 50;
        if (moved || !IsLoaded)
        {
            int version = ++transitionVersion;
            navigationPosition.BeginAnimation(TranslateTransform.YProperty, null);
            navigationPosition.Y = end;
            if (animate)
            {
                var slide = new DoubleAnimation(start, end, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
                slide.Completed += (_, _) =>
                {
                    if (version != transitionVersion)
                        return;
                    navigationPosition.BeginAnimation(TranslateTransform.YProperty, null);
                    foreach (var page in pages)
                    {
                        page.BeginAnimation(OpacityProperty, null);
                        ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
                    }
                };
                navigationPosition.BeginAnimation(TranslateTransform.YProperty, slide);
            }
        }
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var shift = (TranslateTransform)page.RenderTransform;
            if (moved || !IsLoaded)
            {
                page.BeginAnimation(OpacityProperty, null);
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                shift.X = 0;
                page.Opacity = 1;
            }
            page.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            navigationLabels[i].Foreground = i == index ? Ui.Text : Ui.Muted;
            if (animate && i == index)
            {
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 14, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }
        }
        pageTitle.Text = Strings.T(titles[index]);
        pageNote.Text = Strings.T(notes[index]);
        if (index == 2)
            RefreshRows();
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
    public void OpenAppearanceForTest() => appearance?.OpenForTest();
    public void CloseAppearanceForTest() => appearance?.CloseForTest();
    public UIElement? AppearancePreviewForTest => appearance?.PreviewForTest;
    public void ShowTimeline() => timeline?.BringIntoView();
    public void VerifyTimelineForTest(string output) => timeline?.VerifyForTest(output);
    public void PreviewTimelineForTest() => timeline?.PreviewForTest();
    public void PreviewLowTrafficForTest(bool detail) => timeline?.PreviewLowTrafficForTest(detail);
    public string TimelineLayoutForTest => timeline?.LayoutForTest ?? "";

    public void ScrollCurrentToEnd()
    {
        foreach (var page in pages)
            if (page.Visibility == Visibility.Visible)
                ((ScrollViewer)page).ScrollToEnd();
    }

    public bool HasPendingChanges => pendingSave;

    void Changed()
    {
        app.Apply();
        pendingSave = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    void FlushSave()
    {
        if (!pendingSave)
            return;
        pendingSave = false;
        try
        {
            app.Save();
        }
        catch (Exception ex)
        {
            if (!app.IsStopping)
                MessageBox.Show(this, Strings.T(ex.Message), Strings.T("设置保存失败"));
        }
    }

    void Toggle(StackPanel parent, string title, string note, Func<bool> value, Action<bool> changed, bool card = true)
    {
        var toggle = new CheckBox { IsChecked = value(), Margin = new Thickness(18, 0, 0, 0) };
        AccessibleName(toggle, title, note);
        checks.Add((toggle, value));
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        var text = new StackPanel();
        text.Children.Add(Ui.Label(title, 12));
        text.Children.Add(Ui.Label(note, 10.5, Ui.Muted));
        row.Children.Add(text);

        void Apply()
        {
            if (syncing)
                return;
            try
            {
                changed(toggle.IsChecked == true);
                Changed();
            }
            catch (Exception ex) { MessageBox.Show(this, Strings.T(ex.Message), Strings.T("设置失败")); }
        }
        toggle.Checked += (_, _) => Apply();
        toggle.Unchecked += (_, _) => Apply();
        parent.Children.Add(card ? Ui.Card(row) : row);
    }

    void Slider(StackPanel parent, string title, double min, double max, double value, Action<double> changed, string note)
    {
        var header = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        var readout = Ui.Label(value.ToString("P0"), 11, Ui.Down);
        DockPanel.SetDock(readout, Dock.Right);
        header.Children.Add(readout);
        header.Children.Add(Ui.Label(title, 12));
        parent.Children.Add(header);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Margin = new Thickness(0, 4, 0, 2),
            TickFrequency = 0.05,
            SmallChange = 0.05,
            IsSnapToTickEnabled = true
        };
        AccessibleName(slider, title);
        slider.ValueChanged += (_, _) =>
        {
            readout.Text = slider.Value.ToString("P0");
            changed(slider.Value);
            Changed();
        };
        parent.Children.Add(slider);
        parent.Children.Add(Ui.Label(note, 10.5, Ui.Muted));
    }

    void Number(StackPanel parent, string title, double value, string unit, double min, double max, Action<double> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(106)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(44)
        });
        var label = Ui.Label(title, 12);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var box = new TextBox { Text = value.ToString("0.##"), VerticalContentAlignment = VerticalAlignment.Center };
        AccessibleName(box, title);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        var suffix = Ui.Label(unit, 10.5, Ui.Muted);
        suffix.VerticalAlignment = VerticalAlignment.Center;
        suffix.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(suffix, 2);
        grid.Children.Add(suffix);

        void Commit()
        {
            if (double.TryParse(box.Text, out var number) && double.IsFinite(number) && number >= min && number <= max)
            {
                if (number != value)
                {
                    value = number;
                    changed(number);
                    Changed();
                }
            }
            else
            {
                box.Text = value.ToString("0.##");
                box.ToolTip = Strings.Format("输入 {0} 到 {1} 之间的数值", min, max);
            }
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        parent.Children.Add(grid);
    }

    public void SetTheme(string mode)
    {
        app.Config.ColorMode = AppTheme.Normalize(mode);
        Ui.Theme(this, app.Config.ColorMode);
        timeline?.ApplyTheme();
        appearance?.Refresh(true);
        Changed();
    }

    void Refresh()
    {
        Ui.Theme(this, app.Config.ColorMode);
        timeline?.ApplyTheme();
        if (Strings.Language != app.Config.Language)
        {
            ApplyLanguage();
            return;
        }
        syncing = true;
        foreach (var pair in checks)
            pair.Box.IsChecked = pair.Value();
        syncing = false;
        connection.Text = app.Status.StartsWith("内核在线 · ") || app.Status.StartsWith("Core online · ") ||
            app.Status.StartsWith("已连接 · ") || app.Status.StartsWith("Connected · ")
            ? Strings.T("内核在线 · ") + Strings.T(app.Source) : Strings.T(app.Status);
        connection.Foreground = app.Connected ? Ui.Down : Ui.Muted;
        if (pages[2].Visibility == Visibility.Visible && DateTime.UtcNow - lastStats > TimeSpan.FromSeconds(5))
            RefreshRows();
    }

    void RefreshRows()
    {
        if (rows.View == null)
            return;
        try
        {
            var data = app.Store.Query(DateTime.Today.AddDays(range.SelectedIndex == 1 ? -6 : 0), DateTime.Today);
            total.Text = $"↓ {Display.Bytes(data.Sum(row => row.Download))}    ↑ {Display.Bytes(data.Sum(row => row.Upload))}";
            IEnumerable<DailyRow> ordered = sort.SelectedIndex switch
            {
                1 => data.OrderByDescending(row => row.Download),
                2 => data.OrderByDescending(row => row.Upload),
                _ => data
            };
            rows.ItemsSource = ordered.Select(row => new
            {
                row.Target,
                Down = Display.Bytes(row.Download),
                Up = Display.Bytes(row.Upload)
            }).ToList();
            emptyRows.Visibility = rows.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            timeline?.UpdateTargets(range.SelectedIndex == 1 ? data : app.Store.Query(DateTime.Today.AddDays(-6), DateTime.Today));
            timeline?.Refresh(true);
            lastStats = DateTime.UtcNow;
        }
        catch
        {
            total.Text = Strings.T("记录读取失败");
        }
    }

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
