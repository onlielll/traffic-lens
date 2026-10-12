# 验证报告 · Validation

## 2.1.0 时间线 · 2026-10-12

- x64 与 x86 的核心、存储、接口及展示回归通过；时间线新增 30 项断言，覆盖开关基准、观测覆盖时长、时间戳、分钟边界与舍入、目标筛选、断线、休眠、清空和七天保留。
- 连续模拟 10,000 次采样，当前分钟缓冲最多 11 项（十个轮换目标与总量），未保留完整逐秒历史。
- 中英文界面在 820×650 与 520×360 检查，目标可输入；本地时区转换正确，重复刷新 100 次复用同一图表，点数不增长。设置窗口关闭后子进程退出。
- x64 与 x86 的原生测试完成 10,000 次绘制与 100 次窗口切换，GDI 对象 27→27。
- 开启时间线、使用本机接口进行 300 秒隔离运行，成功读取 300 次，失败 0 次；私有工作集 9.96–25.2 MiB，GDI 对象 27–27。未强制 GC 或清空工作集。
- OxyPlot 三个设置页组件共 796,160 字节（约 0.76 MiB），不加入常驻程序的依赖。
- 三个架构均完成自包含构建。Windows 10 与 ARM64 仍待实机验证。此次新功能的短时运行观察不能替代一小时或更长的运行验证。

The optional timeline passed 30 checks, a 10,000-sample bounded-buffer simulation, bilingual chart checks and a five-minute live run. Chart dependencies are confined to the settings process. Long-duration validation of the new feature remains pending.

## 2.0.0 基线结果

测试日期：2026-10-11，Asia/Shanghai。测试系统：Windows 11 build 26200。

## 功能验证

- x64、x86 各通过 49 项核心断言与 192 项展示逻辑断言，覆盖域名合并、上下行独立排行、DIRECT/未知分类、新连接基准、计数回退、跨午夜与重启持久化。
- SQLite 事务失败回滚及后续写入恢复；HTTP 鉴权、分块响应、失败重连、命名管道与取消。
- x64、x86 原生界面各通过 27 项检查：两种模式拖动、捕获释放、位置记忆、四角边界、吸附、置顶、锁定穿透、解锁与悬停区。
- 各版本完成 10,000 次绘制及 100 次尺寸/模式切换，GDI 对象保持 27→27；关闭设置窗口后独立进程退出。
- 中英文设置四页分别检查 820×650 和 520×360 布局，小窗口可滚动访问全部内容。
- 开关动画取样：x64 位移 16→1.178→0，x86 16→1.169→0，160 ms 过渡；语言切换即时更新现有控件。
- Windows 内置 winsqlite3 的 x86 调用使用 stdcall，包括查询回调；有数据的 32 位统计查询与设置页正常。参见 [SQLite 维护者说明](https://sqlite.org/forum/info/2438e82a4d236c48)。

## 一小时连续运行

正式安装版通过本机 Mihomo 命名管道持续读取 3,600 秒，成功 3,601 次，失败 0 次。关闭设置页测量常驻进程，未强制 GC 或清空工作集。

| 指标 | 实测 |
| --- | --- |
| 私有工作集 | 12.25–31.05 MiB |
| 最后十分钟私有工作集 | 28.2–29.2 MiB |
| 总工作集 | 51.88–76.51 MiB，结束时 74.66 MiB |
| GDI 对象 | 窗口展开预热后保持 31 |
| 进程句柄 | 356–374 |

私有工作集通过 Windows 性能计数器测量；总工作集包含共享的运行时与系统页面。观察期内内存与绘图资源未出现持续增长，数值随连接数量、DPI 和交互变化。

## 安装与构建

- 最终 x64 安装、卸载均成功，安装后的程序校验与发布目录一致。卸载前后本机设置、统计文件的 SHA-256 一致。
- 已在当前用户程序目录安装并验证启动与桌面快捷方式。
- [GitHub Actions 独立构建](https://github.com/onlielll/traffic-lens/actions/runs/38122574639) 三个架构全部通过，生成自包含安装包、便携包及校验文件；x64 构建包含核心测试。

| 架构 | 运行验证范围 |
| --- | --- |
| x64 | Windows 11 build 26200 |
| x86 | 同一系统的 32 位兼容环境 |
| ARM64 | 交叉构建，尚待设备实测 |

Windows 10 与真实 32 位设备尚待实测。

## 分配量与体积

相同 .NET 10 合成负载（100 个连接、20 个域名，1,000 轮解析、采样与排行）的分配量从 172,872,856 降至 55,320,040 字节，减少约 68%。分配量与常驻内存是不同指标。

x64 合并发布目录约 132 MiB，较两套运行时分别发布的约 208 MiB 减少 36.8%。安装包约 43 MiB，便携包约 59 MiB。

## English summary

- x64 and x86 each pass 49 core and 192 presentation assertions, plus 27 native UI checks. Repeated painting and mode/size changes preserve GDI counts.
- The installed x64 monitor completes a continuous one-hour live API run: 3,601 successful reads and no failures. Private working set peaks at 31.05 MiB and stays within 28.2–29.2 MiB during the final ten minutes. Total working set ends at 74.66 MiB; GDI resources remain stable after warm-up. No forced collection or working-set trimming is used.
- Installation and uninstallation preserve user data. All three GitHub Actions build jobs pass. Runtime tests cover Windows 11 x64 and its x86 compatibility environment; Windows 10, physical 32-bit devices and ARM64 hardware remain untested.
