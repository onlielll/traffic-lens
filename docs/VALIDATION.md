# 验证报告 · Validation

## 2.1.0 时间线与主题 · 2026-10-12

- x64 与 x86 通过 49 项核心、192 项展示逻辑及 50 项时间线断言，覆盖秒级/分钟聚合、区间总量、目标筛选、24 小时滚动删除、旧分钟精度、保存失败回滚与恢复、断线、休眠和清空。
- 连续模拟 10,000 次采样，秒级历史保存到磁盘；常驻内存只保留五秒批次，保存失败也不会留下无限增长的重试队列。
- 新增界面验证：侧栏选中块 220 ms 滑动、内容短过渡、80 次快速切页后仅一个可见页面且动画时钟已释放。主题浮层与中英文字体检查通过，中文实际命中系统微软雅黑；未附带第三方字体文件。
- 使用短促大峰值和低速背景验证图表：默认近十分钟，峰值聚合不丢峰；“突发”尺度不把低速背景放大到整图，“细节”可查看低速变化。平均速率和每分钟用量仍可切换，明暗主题及小窗口布局通过。
- 中英文界面检查 820×650 与 520×360；悬停同时显示上下行，目标可输入，本地时间正确。100 次刷新及明暗主题切换保持缩放、筛选和区间选择，复用同一图表，显示点数和标注数量有界。
- 原生界面测试包含 10,000 次绘制、100 次尺寸/模式切换及 100 次主题切换。首次两种颜色预热后，主题循环的 GDI 对象恒定；独立设置窗口关闭后进程退出。
- 新记录与图表共用原有一秒采样，不增加外网测速、图表后台采样或常驻 WPF。OxyPlot 组件只在设置进程加载。
- 开启秒级记录、通过本机接口进行 300 秒隔离运行：成功 300 次，失败 0 次，GDI 对象始终为 27。运行后段的八次私有工作集测量为 22.32–26.98 MiB，未强制 GC 或清空工作集。

### 存储与查询实测

Windows 11 build 26200，五秒一次事务，连续写入模拟目标；关闭数据库后测量 SQLite 文件。查询包含总量或指定目标，并按可见范围聚合。

| 持续活跃目标 | 模拟时长 | 数据库 | 五秒写入批次均值 / P95 | 总量 / 单目标查询 |
| --- | --- | --- | --- | --- |
| 0 | 30 分钟 | 0.06 MiB | 2.01 / 2.08 ms | 3.03 / 1.61 ms |
| 5 | 完整 24 小时 | 14.40 MiB | 2.53 / 3.07 ms | 41.02 / 56.26 ms |
| 20 | 30 分钟 | 1.17 MiB | 3.47 / 4.93 ms | 2.01 / 2.25 ms |

20 个持续活跃目标按此数据线性估算约 56.25 MiB/天，属于估算。空闲时只保存观测覆盖信息，没有目标明细；目标名称复用整数 ID。滚动删除后的空页供后续记录复用，文件不必每天缩小。

三种架构均完成自包含构建。x64、x86 在本机验证，Windows 10 与 ARM64 仍待实机验证。五分钟观察、重复交互和模拟一天的数据不能替代新功能的一小时或更长实机运行。

The optional timeline retains complete second-level records for 24 hours. Tests cover exact interval totals, zoom aggregation, legacy precision, bounded batches and themes. A full-day simulation with five active targets used 14.40 MiB; the full-day chart query took 41.02 ms. Long-duration validation of this new feature remains pending.

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
