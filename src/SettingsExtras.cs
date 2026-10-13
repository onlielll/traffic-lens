using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ProxyTrafficFloat;

public sealed partial class SettingsWindow
{
    readonly CancellationTokenSource featureStop = new();
    Task? dataTask;
    bool closingReads;
    NodeStatusPanel? nodePanel;
    ReleaseInfo? availableRelease;
    TextBlock? updateStatus;
    Button? releaseButton;

    void BuildNodePanel(StackPanel page)
    {
        var card = Section("节点延迟", "读取最近活跃节点的已有测速；留空时跟随最快连接所用节点。");
        Toggle(card, "读取节点延迟", "最多保留最近 60 次测速结果，不记录到数据库。",
            () => app.Config.MonitorNodeLatency, value => app.Config.MonitorNodeLatency = value, false);
        nodePanel = new NodeStatusPanel(app);
        card.Children.Add(nodePanel);
        var node = new TextBox { Text = app.Config.LatencyNode, Margin = new Thickness(0, 8, 0, 8) };
        AccessibleName(node, "指定节点名（可留空）");
        node.TextChanged += (_, _) => { app.Config.LatencyNode = node.Text.Trim(); Changed(); };
        card.Children.Add(Ui.Label("指定节点名（可留空）", 11, Ui.Muted));
        card.Children.Add(node);
        Number(card, "慢节点阈值", app.Config.SlowNodeMs, "ms", 50, 10000, value => app.Config.SlowNodeMs = value);
        Toggle(card, "主动测速", "每分钟检测一次；会通过该节点请求 gstatic 的小型测试页面。默认关闭。",
            () => app.Config.ProbeNodeLatency, value => app.Config.ProbeNodeLatency = value, false);
        Toggle(card, "在悬浮窗显示节点趋势", "展开时显示延迟曲线；连续两次超过阈值时提示节点较慢。",
            () => app.Config.ShowNodeLatency, value => app.Config.ShowNodeLatency = value, false);
        page.Children.Add(Ui.Card(card));
    }

    void BuildDataPanel(StackPanel page)
    {
        var card = Section("导出与备份", "导出每日用量或秒级时间线；备份包含完整本地统计，不含接口密钥。");
        var buttons = new WrapPanel();
        var status = Ui.Label("", 11, Ui.Muted); status.Tag = null;
        Add("导出用量 CSV", ".csv", "CSV|*.csv", (store, file) => store.ExportCsv(file, false, featureStop.Token));
        Add("导出时间线 CSV", ".csv", "CSV|*.csv", (store, file) => store.ExportCsv(file, true, featureStop.Token));
        Add("备份数据", ".sqlite", "SQLite|*.sqlite", (store, file) => store.Backup(file));
        card.Children.Add(buttons); card.Children.Add(status);
        page.Children.Add(Ui.Card(card));
        void Add(string title, string extension, string filter, Action<TrafficStore, string> action)
        {
            var button = Ui.Button(title, async (_, _) =>
            {
                var dialog = new SaveFileDialog { FileName = "TrafficLens-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + extension, DefaultExt = extension, Filter = filter };
                if (dialog.ShowDialog(this) != true) return;
                buttons.IsEnabled = false;
                status.Text = Strings.T("正在保存…");
                string database = Path.Combine(app.DataDirectory, "traffic.sqlite");
                dataTask = Task.Run(() => { featureStop.Token.ThrowIfCancellationRequested(); using var store = TrafficStore.OpenReadOnly(database, featureStop.Token); action(store, dialog.FileName); }, featureStop.Token);
                try { await dataTask; status.Text = Strings.T("已保存"); }
                catch (OperationCanceledException) { status.Text = ""; }
                catch (IOException error) { status.Text = Strings.T("保存失败，请重试。"); Diagnostics.Write(app.DataDirectory, "data-export", error); }
                catch (UnauthorizedAccessException error) { status.Text = Strings.T("保存失败，请重试。"); Diagnostics.Write(app.DataDirectory, "data-export", error); }
                finally { buttons.IsEnabled = true; }
            });
            buttons.Children.Add(button);
        }
    }

    void BuildUpdatePanel(StackPanel page)
    {
        var card = Section("版本与更新", "手动检查 GitHub Releases；只在点击时联网，不自动下载或安装。");
        Version local = typeof(SettingsWindow).Assembly.GetName().Version!;
        card.Children.Add(Ui.Label("Traffic Lens " + local.ToString(3), 12));
        updateStatus = Ui.Label("", 11, Ui.Muted); updateStatus.Tag = null;
        Toggle(card, "包含预览版", "同时检查尚未正式发布的公开预览版本。", () => app.Config.PreviewUpdates, value => app.Config.PreviewUpdates = value, false);
        var buttons = new WrapPanel();
        var check = Ui.Button("检查更新", async (sender, _) =>
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            updateStatus.Text = Strings.T("正在检查…");
            try
            {
                availableRelease = await ReleaseCheck.FetchAsync(app.Config.PreviewUpdates, featureStop.Token);
                bool newer = availableRelease != null && availableRelease.Version > local;
                updateStatus.Text = newer ? Strings.Format("发现新版本 {0}", availableRelease!.Tag) : Strings.T("暂无更高版本");
                releaseButton!.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (featureStop.IsCancellationRequested) { }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                updateStatus.Text = Strings.T("检查失败，请稍后重试。");
                Diagnostics.Write(app.DataDirectory, "update-check", error);
            }
            finally { button.IsEnabled = true; }
        });
        releaseButton = Ui.Button("查看版本", (_, _) =>
        {
            if (availableRelease != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(availableRelease.Page.AbsoluteUri) { UseShellExecute = true });
        });
        releaseButton.Visibility = Visibility.Collapsed;
        buttons.Children.Add(check); buttons.Children.Add(releaseButton);
        card.Children.Add(buttons); card.Children.Add(updateStatus);
        page.Children.Add(Ui.Card(card));
    }

    async void FinishDataBeforeClose(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closingReads) { e.Cancel = true; return; }
        if (dataTask is not { IsCompleted: false } && rowsReadTask is not { IsCompleted: false } && timeline?.PendingRead is not { IsCompleted: false }) return;
        e.Cancel = true;
        closingReads = true;
        featureStop.Cancel();
        try { await Task.WhenAll(dataTask ?? Task.CompletedTask, rowsReadTask ?? Task.CompletedTask, timeline?.PendingRead ?? Task.CompletedTask); }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException) { }
        closingReads = false;
        Close();
    }
}