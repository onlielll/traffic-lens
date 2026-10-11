; Build from scripts/package.ps1 after the two self-contained applications are merged.
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef SourcePath
  #define SourcePath "..\.build\publish\" + Runtime
#endif
#ifndef OutputPath
  #define OutputPath "..\dist"
#endif

[Setup]
AppId={{9C84E5E2-9472-4AD9-95C8-F35D9C39A060}
AppName=Traffic Lens
AppVersion={#AppVersion}
AppPublisher=onlielll
AppCopyright=Copyright (c) 2026 onlielll
DefaultDirName={localappdata}\Programs\TrafficLens
DefaultGroupName=Traffic Lens
PrivilegesRequired=lowest
MinVersion=10.0.14393
#if Runtime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Runtime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x86compatible
#endif
OutputDir={#OutputPath}
OutputBaseFilename=TrafficLens-{#AppVersion}-{#Runtime}-setup
SetupIconFile=..\assets\traffic.ico
UninstallDisplayIcon={app}\ProxyTrafficFloat.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
DisableWelcomePage=no
DisableProgramGroupPage=yes
; Restart Manager checks only our installed executables, without force termination.
CloseApplications=yes
CloseApplicationsFilter=ProxyTrafficFloat.exe,ProxyTrafficSettings.exe
RestartApplications=no
UsePreviousAppDir=yes
UninstallDisplayName=Traffic Lens

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zh"; MessagesFile: "compiler:Default.isl"

[LangOptions]
zh.LanguageName=简体中文
zh.LanguageID=$0804
zh.LanguageCodePage=936
zh.DialogFontName=Microsoft YaHei UI
zh.WelcomeFontName=Microsoft YaHei UI

[Messages]
zh.SetupAppTitle=安装
zh.SetupWindowTitle=安装 - %1
zh.UninstallAppTitle=卸载
zh.UninstallAppFullTitle=卸载 %1
zh.InformationTitle=提示
zh.ConfirmTitle=确认
zh.ErrorTitle=错误
zh.ButtonBack=< 上一步(&B)
zh.ButtonNext=下一步(&N) >
zh.ButtonInstall=安装(&I)
zh.ButtonOK=确定
zh.ButtonCancel=取消
zh.ButtonYes=是(&Y)
zh.ButtonNo=否(&N)
zh.ButtonFinish=完成(&F)
zh.ButtonBrowse=浏览(&B)...
zh.SelectLanguageTitle=选择安装语言
zh.SelectLanguageLabel=请选择安装向导使用的语言。
zh.WelcomeLabel1=欢迎安装 [name]
zh.WelcomeLabel2=将为当前 Windows 用户安装 [name/ver]。%n%n运行环境已包含在安装包中，无需另装 .NET。安装前将正常关闭现有 Traffic Lens；设置和流量记录会保留。
zh.ClickNext=点击“下一步”继续，或点击“取消”退出安装。
zh.WizardSelectDir=选择安装位置
zh.SelectDirDesc=将 [name] 安装在哪里？
zh.SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
zh.SelectDirBrowseLabel=点击“下一步”继续，也可以点击“浏览”更改位置。
zh.DiskSpaceMBLabel=至少需要 [mb] MB 可用空间。
zh.DiskSpaceGBLabel=至少需要 [gb] GB 可用空间。
zh.WizardSelectTasks=快捷方式
zh.SelectTasksDesc=是否创建桌面快捷方式？
zh.SelectTasksLabel2=选择需要的快捷方式，然后点击“下一步”。
zh.WizardReady=准备安装
zh.ReadyLabel1=安装程序已准备将 [name] 安装到电脑。
zh.ReadyLabel2a=点击“安装”继续，或点击“上一步”检查设置。
zh.ReadyMemoDir=安装位置：
zh.ReadyMemoTasks=快捷方式：
zh.WizardPreparing=准备安装
zh.PreparingDesc=正在准备安装 [name]。
zh.ApplicationsFound=以下程序正在使用待更新文件。请允许安装程序正常关闭它们，或自行从托盘退出 Traffic Lens。
zh.CloseApplications=正常关闭程序(&A)
zh.DontCloseApplications=暂不关闭程序(&D)
zh.ErrorCloseApplications=未能正常关闭程序。请从 Traffic Lens 托盘退出后重试。
zh.WizardInstalling=正在安装
zh.InstallingLabel=正在安装 [name]，请稍候。
zh.StatusClosingApplications=正在关闭 Traffic Lens...
zh.StatusExtractFiles=正在释放程序文件...
zh.StatusCreateIcons=正在创建快捷方式...
zh.StatusSavingUninstall=正在保存卸载信息...
zh.FinishedHeadingLabel=[name] 安装完成
zh.FinishedLabel=已安装 [name]，可通过开始菜单或桌面快捷方式启动。%n%n请保持 Mihomo 或 Clash Verge 运行。
zh.FinishedLabelNoIcons=已安装 [name]。请保持 Mihomo 或 Clash Verge 运行。
zh.ClickFinish=点击“完成”退出安装向导。
zh.ExitSetupTitle=退出安装
zh.ExitSetupMessage=安装尚未完成。现在退出后，可以再次运行安装包。%n%n确认退出？
zh.WindowsVersionNotSupported=此程序面向 Windows 10 和 Windows 11。请检查系统版本及下载包架构。
zh.OnlyOnTheseArchitectures=请下载适合以下处理器架构的安装包：%n%n%1
zh.ConfirmUninstall=确认卸载 %1？%n%n设置和流量记录会保留在本机。
zh.WizardUninstalling=正在卸载
zh.UninstallStatusLabel=正在卸载 %1，请稍候。
zh.StatusUninstalling=正在卸载 %1...
zh.UninstalledAll=已卸载 %1。设置和流量记录已保留。
zh.UninstalledMost=%1 卸载完成。部分正在使用的文件未能移除，可以稍后手动删除。%n%n设置和流量记录已保留。

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
zh.DesktopShortcut=创建桌面快捷方式
en.Shortcuts=Shortcuts
zh.Shortcuts=快捷方式
en.Launch=Open Traffic Lens
zh.Launch=启动 Traffic Lens
en.CloseMonitor=Please exit Traffic Lens from its tray menu, then run this installer again.
zh.CloseMonitor=请先从 Traffic Lens 托盘退出程序，再重新运行安装包。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\Traffic Lens"; Filename: "{app}\ProxyTrafficFloat.exe"
Name: "{group}\Uninstall Traffic Lens"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Traffic Lens"; Filename: "{app}\ProxyTrafficFloat.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ProxyTrafficFloat.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[Code]
function GetAncestor(Window: HWND; Flags: Cardinal): HWND;
  external 'GetAncestor@user32.dll stdcall';

function InitializeSetup(): Boolean;
var
  Window: HWND;
  Attempt: Integer;
begin
  { An older portable copy can live elsewhere. Close only our window class.
    GA_ROOTOWNER resolves the small unlock popup to its owning main window. }
  Window := FindWindowByClassName('ProxyTrafficFloat.NativeWindow');
  if Window <> 0 then
  begin
    Window := GetAncestor(Window, 3);
    SendMessage(Window, $0010, 0, 0); { WM_CLOSE, never force-kill a process }
    for Attempt := 1 to 50 do
    begin
      if FindWindowByClassName('ProxyTrafficFloat.NativeWindow') = 0 then Break;
      Sleep(100);
    end;
  end;
  Result := FindWindowByClassName('ProxyTrafficFloat.NativeWindow') = 0;
  if not Result then MsgBox(CustomMessage('CloseMonitor'), mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, Executable: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove startup only when it points at this installed copy. User data stays. }
    Executable := ExpandConstant('{app}\ProxyTrafficFloat.exe');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'ProxyTrafficFloat', Command) then
      if (CompareText(Trim(Command), '"' + Executable + '"') = 0) or
        (CompareText(Trim(Command), Executable) = 0) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ProxyTrafficFloat');
  end;
end;
