# 版本说明 · Release notes

## 首次 Release · v2.0.0 preview

Windows 上的 Mihomo 代理流量监控悬浮窗：实时查看流量来源，回看每日用量。

**[下载 Windows x64 安装包](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-x64-setup.exe)** · [便携版](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-x64-portable.zip) · [使用说明](https://github.com/onlielll/traffic-lens#readme)

![Traffic Lens](https://raw.githubusercontent.com/onlielll/traffic-lens/main/docs/images/overview.png)

### 主要功能

- 下载、上传最快目标分别排行，支持域名合并或单连接查看。
- 每日域名用量保留 30 天，提供今天与近七天排行。
- 迷你模式与阈值自动展开，支持边缘吸附、锁定穿透、托盘控制和中英文。

运行环境已包含，无需另装 .NET。保持 Clash Verge 或 Mihomo 内核运行即可开始监控。

<details>
<summary>其他架构下载 / Other architectures</summary>

| 架构 / Architecture | 安装包 / Installer | 便携版 / Portable |
| --- | --- | --- |
| x86 | [EXE](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-x86-setup.exe) | [ZIP](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-x86-portable.zip) |
| ARM64 | [EXE](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-arm64-setup.exe) | [ZIP](https://github.com/onlielll/traffic-lens/releases/download/v2.0.0/TrafficLens-2.0.0-win-arm64-portable.zip) |

</details>

<details>
<summary>English</summary>

A Windows overlay for Mihomo proxy traffic. Find the busiest targets and review daily usage.

- Separate download and upload leaders, grouped by domain or ranked by connection.
- Daily domain usage retained for 30 days, with rankings for today and the past seven days.
- Mini view, threshold-based expansion, edge snapping, click-through locking, tray controls, and Chinese/English preferences.

The runtime is included. Keep the Clash Verge or Mihomo core running to monitor traffic. [English guide](https://github.com/onlielll/traffic-lens/blob/main/README.en.md)

</details>

### 版本状态 / Release status

**v2.0.0 公开预览版 / Public preview.** 面向 Windows 10 / 11，已在 Windows 11 验证 x64 与 x86 兼容环境；其他设备的实测范围见 [验证报告 / Validation](https://github.com/onlielll/traffic-lens/blob/v2.0.0/docs/VALIDATION.md)。

监测累计仅包含运行期间观测到的代理流量；短连接与断线期间可能漏记。 / Observed totals cover traffic seen while monitoring; short connections and disconnected periods may be missed.

[反馈 / Report an issue](https://github.com/onlielll/traffic-lens/issues) · [MIT](https://github.com/onlielll/traffic-lens/blob/v2.0.0/LICENSE)
