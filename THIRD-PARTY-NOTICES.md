# 第三方许可与设计参考

应用源码及原创图标使用仓库的 MIT LICENSE。

悬浮窗工具栏采用 [Heroicons](https://github.com/tailwindlabs/heroicons) 的 20 px solid 图标，版权归 Tailwind Labs, Inc.，使用 MIT 许可。原始 SVG、上游版本及许可保存在 `assets/heroicons/`，完整许可随成品保留在 `THIRD-PARTY-NOTICES-HEROICONS.txt`。

发布包携带 Microsoft .NET 10 运行组件，其 MIT 许可与第三方声明随包保留：
- `THIRD-PARTY-NOTICES-DOTNET.txt`
- `THIRD-PARTY-NOTICES-RUNTIME.txt`

构建安装器使用 Inno Setup 6.7.3。非商业构建遵守其许可；商业分发安装器请核对 Inno Setup 的最新商业许可。安装器编译器属于开发工具，不包含在应用便携包中。

UI 的层次与间距参考 Windows Fluent 和 ModernFlyouts，图标光学重量和排列参考 [Apple HIG](https://developer.apple.com/design/human-interface-guidelines/sf-symbols)。工具栏图标由系统 GDI+ 填充矢量路径绘制；托盘图标为项目原创，生成源码见 `assets/Generate-Icon.ps1`。
