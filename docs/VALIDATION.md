# 阶段验收 · Validation

记录时间：2026-10-11，Asia/Shanghai。该文件记录实际完成的检查，不代表全部 Windows 和硬件已经实测。

## 已完成 · Passed

- Windows 11 build 26200：x64 与 x86 自包含程序运行；ARM64 交叉构建完成，尚未硬件实测。
- x64 与 x86 各通过 49 项核心断言、192 项展示逻辑断言，包含域名合并、上下行分别排行、DIRECT/未知分类、新连接基准、计数回退、跨午夜和重启持久化。
- SQLite 事务失败回滚及下一次写入恢复；模拟 HTTP 鉴权、分块响应、失败重连、命名管道和取消。
- 最新 x64 与 x86 原生界面各通过 27 项检查：两种模式拖动、捕获释放、位置记忆、四角边界、吸附、置顶、锁定穿透、解锁和悬停区。
- 各版本 10,000 次绘制与 100 次尺寸/模式切换后，GDI 对象保持 27→27；设置窗口关闭后独立进程退出。
- 中英文各四页设置，在 820×650 与 520×360 下出图检查；小窗口通过滚动访问完整内容。
- 开关动画实际取样：x64 位移 16→1.178→0，x86 16→1.169→0，160 ms 过渡；语言切换即时更新现有控件。
- 修复 Windows 内置 winsqlite3 的 x86 stdcall 调用约定。补充有数据的统计查询后，32 位设置页与核心测试正常。详见 [SQLite 维护者说明](https://sqlite.org/forum/info/2438e82a4d236c48)。
- 最终 x64 安装与卸载均返回成功；安装后的程序与发布目录校验一致。卸载前后，本机设置和统计文件的 SHA-256 完全一致。随后安装到当前用户程序目录并创建桌面快捷方式。
- GitHub Actions 首次独立构建通过：x64、x86、ARM64 均生成自包含安装包、便携包及校验文件；x64 构建包含核心测试。见 [构建记录](https://github.com/onlielll/traffic-lens/actions/runs/38122574639)。这不是 ARM64 硬件运行验收。

## 内存与体积 · Memory and package size

关闭设置页后测量常驻进程，使用 Windows 私有工作集计数器；没有定时强制 GC 或清空工作集。
本轮约 26 分钟现场运行，接口失败为 0。采样私有工作集约 14–30 MiB，后段约 20–21 MiB；总工作集包含可共享的运行时和系统页面，约 63–72 MiB。数字随连接数量、DPI、系统回收和交互而变化。
一小时最终连续验收尚未完成：为修复 32 位调用约定并完成阶段性暂停，本轮日志保留在开发工作区，恢复后需重新执行或检查后续长测。

相同 .NET 10 环境的合成采样基准（100 个连接、20 个域名，1,000 轮 JSON 解析、采样、排行）分配量从 172,872,856 降至 55,320,040 字节，减少约 68%。这衡量分配量，不等同于常驻内存。
合并运行时后的 x64 发布目录约 132 MiB，相比两套程序分别附带运行时的约 208 MiB 减少约 36.8%。安装包约 43 MiB，便携包约 59 MiB。

## 尚待完成 · Remaining checks

- 最终版本一小时连续运行、内存与句柄趋势。
- 跨电脑兼容性：Windows 10、真实 32 位设备和 ARM64 设备实测。
- 首次预览 Release 附件与发布；源码及中英文说明已上传公开仓库，等待一小时测试结果后整理附件。

These are stage results, not a guarantee of zero leaks or universal compatibility. Final x64 installation and uninstallation preserve user data, and all three GitHub Actions build jobs pass. The final one-hour soak, other-device checks and preview release attachments are still pending.
