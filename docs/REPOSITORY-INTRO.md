# 仓库介绍 · Repository introduction

推荐仓库名 / Suggested name: `traffic-lens`

## GitHub Description

> Windows 上的 Mihomo 流量监控悬浮窗：实时排行与每日统计。Mihomo traffic overlay for Windows: live rankings and daily usage.

Suggested topics: `windows`, `mihomo`, `clash`, `traffic-monitor`, `desktop`, `csharp`.

## 首次 Release · v2.0.0 preview

Traffic Lens 是 Windows 上的 Mihomo 代理流量监控悬浮窗，提供实时上下行排行、每日域名用量和可自动收起的桌面视图。

Traffic Lens is a Windows overlay for Mihomo proxy traffic, with live upload/download rankings, daily domain usage, and an automatically collapsing desktop view.

小窗可以拖动、吸附、锁定和自动收起；速度超过阈值时再展开。设置支持中文和英文，运行环境已包含在下载包中，无需另装 .NET 8 或 .NET 10。请保持 Mihomo 或 Clash Verge 运行。

Drag, snap, lock or collapse the overlay, and let it expand when traffic exceeds your thresholds. Settings support Chinese and English. The packages include their runtime, so no separate .NET 8 or .NET 10 installation is needed. Keep Mihomo or Clash Verge running.

### 下载 · Downloads

| 电脑 / Device | 安装包 / Installer |
| --- | --- |
| 常见 Intel/AMD 64 位电脑 / Most Intel/AMD PCs | `TrafficLens-2.0.0-win-x64-setup.exe` |
| 32 位 Windows / 32-bit Windows | `TrafficLens-2.0.0-win-x86-setup.exe` |
| Windows on ARM | `TrafficLens-2.0.0-win-arm64-setup.exe` |

无需安装的便携 ZIP、源码 ZIP 和 `SHA256SUMS.txt` 也随 Release 提供。安装版卸载后保留本机设置和统计。

Portable ZIPs, a source ZIP and `SHA256SUMS.txt` are also included. Uninstalling the installed version preserves local settings and statistics.

### 范围与限制 · Scope and limits

程序只读取本机 Mihomo 接口，排除最终 DIRECT 的连接；不修改分流规则，不上传统计，也不主动外网测速。域名缺失时显示 IP，程序名是否可用取决于内核。

Traffic Lens reads only the local Mihomo API and excludes final DIRECT connections. It does not change routing rules, upload statistics or run external speed tests. An IP is shown when no domain is available; process information depends on the core.

“监测累计”只计算运行期间观测到的增量。短连接和断线期间可能漏记，因此适合定位来源，不作为精确计费账单。

Observed totals cover only traffic seen while monitoring. Short connections and disconnected periods may be missed. Use the results to locate traffic sources, rather than as an exact billing record.

最低发行目标为 Windows 10 1607，也面向 Windows 11。x64 与 x86 已在 Windows 11 上运行；ARM64 当前仅交叉构建。测试方法与结果见 [验证报告](VALIDATION.md)。

The minimum release target is Windows 10 1607, with Windows 11 also targeted. x64 and x86 have run on Windows 11; ARM64 is currently cross-built only. See the [validation report](VALIDATION.md) for the tested scope.

Copyright © 2026 **onlielll**. Source code is licensed under MIT. Heroicons and bundled runtime notices are retained in [third-party notices](../THIRD-PARTY-NOTICES.md).
