# Traffic Lens · 代理流量

不用打开 Clash 连接表，随时看到哪个代理目标正在消耗带宽。

Traffic Lens 是面向 Windows 的轻量桌面悬浮窗，读取 Mihomo 的本地接口，分别显示下载和上传最快的域名、IP、节点及可获取的程序名称，并保存每日代理流量排行。

See what's using your proxy bandwidth without keeping the connections page open. Traffic Lens shows the fastest upload and download targets in a compact Windows overlay and keeps observed daily usage on your computer.

**2.0.0 公开预览版**提供 Windows 10/11 的安装包和便携包。运行环境已随包附带，无需另装 .NET 8 或 .NET 10。[English](README.en.md)

![详细悬浮窗](docs/images/overlay.png)
![迷你悬浮窗](docs/images/mini.png)
![设置界面](docs/images/settings.png)

截图使用示例域名，不含个人流量记录。

## 功能

- 上下行最快目标分别排名；按域名合并或查看单连接，无域名时使用目标 IP。
- 根据实际最终出站排除 DIRECT，无法识别的链路单列为未知。
- 详细窗与迷你窗，可拖动、边缘吸附、记住位置、调节透明度及置顶。
- 点击小锁锁定；默认锁定后点击穿透，解锁按钮和托盘仍可操作。
- 超过速度阈值自动展开，空闲后收起；展示内容可独立开关。
- 本地每日统计、今天/近七天排行，默认保留 30 天。
- 托盘运行、可选开机启动、重复启动唤起已有实例。
- 中文 / English / 跟随系统，切换立即生效；设置开关有平滑滑块与底色过渡。

## 下载与运行

在本仓库的 **Releases** 中下载适合电脑架构的附件：

| 电脑 | 安装包 | 便携包 |
| --- | --- | --- |
| 常见 Intel/AMD 64 位电脑 | `TrafficLens-2.0.0-win-x64-setup.exe` | `TrafficLens-2.0.0-win-x64-portable.zip` |
| 32 位 Windows | `TrafficLens-2.0.0-win-x86-setup.exe` | `TrafficLens-2.0.0-win-x86-portable.zip` |
| Windows on ARM | `TrafficLens-2.0.0-win-arm64-setup.exe` | `TrafficLens-2.0.0-win-arm64-portable.zip` |

安装包为当前 Windows 用户安装，提供开始菜单入口和可选桌面快捷方式，无需管理员权限。便携包解压后双击 `ProxyTrafficFloat.exe`，保持同目录文件完整。关闭设置页后仍继续监控；从托盘选择退出才停止。卸载安装版保留本机设置和流量记录。

保持 Mihomo 或 Clash Verge 运行。程序自动发现本机 Mihomo 命名管道；连接失败时从托盘打开设置，填写本机 HTTP 地址（如 `http://127.0.0.1:9090`）或命名管道，按需输入接口密钥。

GitHub 自动生成的 “Source code” 是源码，不能直接运行。整理好的源码附件是 `TrafficLens-2.0.0-source.zip`。

## 兼容性与资源占用

最低发行目标为 Windows 10 1607（build 14393），也面向 Windows 11；这不是全部历史系统和硬件已经实测的承诺。微软对 .NET 10 的维护支持范围取决于系统生命周期，目前 Windows 10 主要为仍受维护的 Enterprise/LTSC 系列，与应用的运行目标不同。[微软平台说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

| 架构 | 当前验证范围 |
| --- | --- |
| x64 | Windows 11 build 26200 上测试了接口、悬浮窗、设置与发布包运行 |
| x86 | 在上述 x64 系统的 32 位兼容环境中运行；尚未在真实 32 位 Windows 10 电脑实测 |
| ARM64 | 发布包已交叉构建；尚未在 ARM64 设备实测 |

常驻程序使用 Win32/GDI 绘制，不加载 WPF；WPF 设置页仅在打开时运行，关闭后退出。两个程序共享一份随包发布的 .NET 10.0.12 运行时。x64 发布目录约 132 MiB，比两套运行时分别打包的约 208 MiB 减少约 36.8%。x64 便携 ZIP 约 59 MiB，安装包约 43 MiB，具体以附件为准。

自包含包比旧版依赖电脑运行时的小包更大，换取免安装环境和离线运行。下载体积不等于常驻内存；内存测试、测试方法和持续运行结果见 [验证报告](docs/VALIDATION.md)。

## 操作

悬浮窗顶部提供设置、模式切换、锁定、隐藏按钮。拖动空白区域移动窗口，接近工作区边缘时吸附；点击迷你窗临时展开。穿透开启后可以从托盘解除。

默认自动展开条件：下载最快目标达到 1 MB/s，或上传最快目标达到 512 KB/s，持续 2 秒。两者都低于阈值一半持续 10 秒后收起。阈值、延时和角落停靠均可修改。

可分别开关总速度、上传/下载最快目标、今日总量和目标累计量。域名涉及多个程序或节点时显示“多个”，缺失程序名显示“未知”。单连接排行中的今日累计仍是该域名/IP 的当日合计。

移动程序目录后，重新设置开机启动以更新路径。开机启动默认关闭。

## 统计含义与隐私

速度每秒采样，使用最近三秒的平均值。“监测累计”只包含软件运行期间实际观测到的字节增量；首次看到连接、断线重连及内核重启时建立新基准，避免重复计数。日期按电脑本地时间划分。

采样间隔内完成的短连接、软件退出或接口中断期间的流量可能漏记。HTTPS 连接无法说明下载的是哪个文件，程序名是否可见取决于内核。统计用于定位来源，不能作为精确计费账单。

设置和 SQLite 数据库保存在 `%LOCALAPPDATA%\ProxyTrafficFloat`。只保存每日域名/IP 上传与下载量，不保存完整 URL 或完整连接历史。接口密钥由 Windows DPAPI 保护。只接受本机接口，不修改分流规则，不断开用户连接，不主动外网测速，也不上传监控统计。

## 开发与打包

开发需要 Windows 与 .NET 10 SDK；`global.json` 使用 10.0.401 并允许兼容的后续 feature band。SDK 只用于开发，成品不要求用户安装它。

```powershell
# 默认构建 x64 自包含版并运行测试
.\build.ps1 -Runtime win-x64 -Test

# 打包便携版、安装版、校验文件与整理后的源码
.\scripts\package.ps1 -Runtime win-x64 -IncludeSource
```

安装包构建需要 Inno Setup 6；可用 `-Compiler` 指定 `ISCC.exe`，或用 `-SkipInstaller` 只制作 ZIP。发布输出位于 `dist/`。其他架构将 `-Runtime` 改为 `win-x86` 或 `win-arm64`。可通过 GitHub Actions 构建三个架构；版本 tag 触发草稿 Release，审核后再发布。

`native/` 是常驻监控，`src/` 是独立设置页和共享统计逻辑，`tests/` 是测试，`assets/` 是原创资源。程序复用绘制资源，减少每秒采样的重复分配，并在退出时释放原生资源。

提交问题或改进前请阅读 [贡献说明](CONTRIBUTING.md)。第一次上传可参考 [GitHub 发布指南](docs/FIRST-GITHUB-UPLOAD.md)，版本变化见 [更新记录](CHANGELOG.md)。

## 许可证与设计参考

Copyright © 2026 **onlielll**。应用源码和原创资源使用 [MIT 许可证](LICENSE)。随包运行时、开源图标及安装器工具的许可见 [第三方说明](THIRD-PARTY-NOTICES.md)。

界面层级与间距参考 [ModernFlyouts](https://github.com/ModernFlyouts-Community/ModernFlyouts) 与 [Windows Fluent 设计指南](https://learn.microsoft.com/en-us/windows/apps/design/)，按钮比例参考 [Apple HIG](https://developer.apple.com/design/human-interface-guidelines/sf-symbols)。悬浮窗工具栏使用 MIT 许可的 [Heroicons](https://github.com/tailwindlabs/heroicons) 实心矢量图标，托盘图标为原创资源。
