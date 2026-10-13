# 版本说明 · Release notes

## 首次 Release · v2.1.0 preview

## Traffic Lens 2.1.0

Windows 上的 Mihomo 代理流量监控悬浮窗。实时定位流量来源，回看用量变化。

**[Windows x64 安装包](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-x64-setup.exe)** · [便携版](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-x64-portable.zip)

### 这个版本新增了什么

- **秒级时间线**：滚动保留最近 24 小时，按域名/IP 查看，切换速率与每分钟用量；平滑曲线、缩放和平移，选择时间区间查看总上传与下载。
- **桌面体验**：浅色/深色/跟随系统，全屏应用自动隐藏，四角调整大小，统一切换动画与更顺滑的设置页滚动。
- **数据工具**：CSV 导出、数据库备份、剩余额度与可用天数估算。
- **节点与更新**：读取已有节点延迟、慢节点提示，可选主动测速，以及手动检查应用更新。

域名排行、迷你模式、阈值自动展开、吸附、锁定穿透和托盘控制继续保留。运行环境已包含，无需另装 .NET。

<details>
<summary>其他架构 / Other architectures</summary>

| 架构 | 安装包 | 便携版 |
| --- | --- | --- |
| x86 | [EXE](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-x86-setup.exe) | [ZIP](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-x86-portable.zip) |
| ARM64 | [EXE](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-arm64-setup.exe) | [ZIP](https://github.com/onlielll/traffic-lens/releases/download/v2.1.0/TrafficLens-2.1.0-win-arm64-portable.zip) |

</details>

<details>
<summary>English</summary>

### What's new

- Per-second history retained for a rolling 24 hours, domain/IP filtering, speed and per-minute usage views, smooth curves, zoom/pan, and interval totals.
- System/light/dark appearance, fullscreen hiding, corner resizing, consistent transitions, and smoother settings scrolling.
- CSV export, database backups, and allowance estimates.
- Existing node latency results, slow-node hints, optional active testing, and manual update checks.

Live domain rankings, mini view, threshold-based expansion, edge snapping, click-through locking, and tray controls remain available. The runtime is included; no separate .NET installation is required.

</details>

### 使用范围 / Scope

**2.1.0 预览版 / Preview.** Windows 11 x64 与 x86 兼容环境已运行本轮核心及界面检查；ARM64 完成交叉构建，尚待设备实测。Windows 10 尚待实测。

监测累计仅覆盖软件运行期间观测到的代理流量，不是套餐账单；额度估算不包含其他设备、离线用量及节点倍率。 / Observed totals cover monitored proxy traffic, not billing. Allowance estimates exclude other devices, offline usage, and node multipliers.

[使用说明 / Guide](https://github.com/onlielll/traffic-lens/tree/release/v2.1-preview#readme) · [问题反馈 / Issues](https://github.com/onlielll/traffic-lens/issues) · [MIT](https://github.com/onlielll/traffic-lens/blob/release/v2.1-preview/LICENSE)
