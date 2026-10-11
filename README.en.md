# Traffic Lens

A compact Windows overlay that tells you which proxy target is using your bandwidth, without opening the Clash connection table. [简体中文](README.md)

**2.0.0 is the first public preview.** Installers and portable packages include .NET 10.0.12: no separate .NET 8 or .NET 10 installation is required.

![Detail view](docs/images/overlay-en.png)
![Mini view](docs/images/mini-en.png)
![Settings](docs/images/settings-en.png)

Screenshots use example domains rather than personal traffic records.

## Features

- Independent download and upload leaders, domain grouping or individual connection ranking, with destination IP as a fallback.
- Exclusion of final DIRECT outbound connections; unknown routes remain separate.
- Detail and mini views, edge snapping, saved positions, opacity and always-on-top controls.
- A small lock button with optional input pass-through for the rest of the overlay.
- Automatic expansion above configurable speed thresholds.
- Local daily totals, today/seven-day rankings, 30-day retention, tray controls, optional startup and single-instance activation.

## Getting started

Download an attachment from this repository's **Releases** for your architecture: `TrafficLens-2.0.0-win-x64-setup.exe`, `TrafficLens-2.0.0-win-x86-setup.exe`, or `TrafficLens-2.0.0-win-arm64-setup.exe`. The matching portable ZIPs end in `-portable.zip`.

The installer runs for the current user without administrator privileges and offers an optional desktop shortcut. For portable use, extract the complete folder and launch `ProxyTrafficFloat.exe`; keep all companion files together. Uninstalling preserves configuration and usage statistics.

Keep Mihomo or Clash Verge running. Local named pipes are discovered automatically; a local HTTP endpoint or pipe and an API secret can be entered in Settings if required. Closing Settings leaves monitoring active; use the tray menu to exit the monitor.

The prepared source package is `TrafficLens-2.0.0-source.zip`. Source archives require building and are not runnable downloads.

## Compatibility and size

The minimum distribution target is Windows 10 1607, build 14393, with Windows 11 also targeted. Not every historical Windows release or device has been tested. Microsoft's .NET support follows the OS lifecycle; maintained Windows 10 Enterprise/LTSC editions differ from the application's broader runtime target. See [Microsoft's platform documentation](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

| Architecture | Validation |
| --- | --- |
| x64 | Tested on Windows 11 build 26200 with the local API, overlay, Settings and packaged execution |
| x86 | Ran in the same x64 system's 32-bit compatibility environment; no physical 32-bit Windows 10 validation yet |
| ARM64 | Cross-built packages; no ARM64 hardware validation yet |

The resident overlay uses Win32/GDI and does not load WPF. The separate WPF Settings process exits when closed. Both share one packaged runtime, reducing the x64 folder from roughly 208 MiB with duplicated runtimes to 132 MiB, about 36.8% smaller. The x64 portable ZIP is approximately 59 MiB and the installer approximately 43 MiB; attachment sizes are authoritative.

Self-contained packages are larger than the old runtime-dependent downloads in exchange for no runtime setup and offline execution. Package size is not resident memory usage. See [validation results](docs/VALIDATION.md) for measurement methods and stability observations.

## Measurement and privacy

Connections are sampled once per second; speeds average the last three seconds. Daily amounts include observed increments during monitoring only. New connections and reconnections establish a baseline rather than recounting earlier usage. Short connections and monitoring gaps may be missed, so totals are diagnostic, not billing figures. Process identification depends on Mihomo, and HTTPS does not reveal the downloaded file.

Configuration and SQLite statistics remain in `%LOCALAPPDATA%\ProxyTrafficFloat`. Only daily domain/IP upload and download totals are retained, not full URLs or connection history. Secrets use Windows DPAPI. Traffic Lens reads local API data and does not change routing, terminate connections, upload statistics or perform external speed tests.

## Development

Use Windows and .NET 10 SDK. `global.json` selects 10.0.401 with compatible feature-band roll-forward. Build and package from the source root:

```powershell
.\build.ps1 -Runtime win-x64 -Test
.\scripts\package.ps1 -Runtime win-x64 -IncludeSource
```

Installer builds require Inno Setup 6; specify `-Compiler` or use `-SkipInstaller` for ZIP-only packaging. Outputs are placed in `dist/`. GitHub Actions builds all three architectures and creates a reviewable draft Release for version tags.

See [CONTRIBUTING](CONTRIBUTING.md), [CHANGELOG](CHANGELOG.md) and the [first GitHub upload guide](docs/FIRST-GITHUB-UPLOAD.md).

## License and visual references

Copyright © 2026 **onlielll**. Application code and original assets are licensed under [MIT](LICENSE). Bundled runtime, open-source icon and installer-tool terms are described in [third-party notices](THIRD-PARTY-NOTICES.md).

Visual hierarchy and spacing draw inspiration from [ModernFlyouts](https://github.com/ModernFlyouts-Community/ModernFlyouts) and [Windows Fluent design](https://learn.microsoft.com/en-us/windows/apps/design/). Toolbar balance follows [Apple HIG](https://developer.apple.com/design/human-interface-guidelines/sf-symbols), using MIT-licensed [Heroicons](https://github.com/tailwindlabs/heroicons) solid vectors. The tray icon is original to this project.

Choose Chinese, English or the system default in Connection settings. Changes apply instantly, including the overlay and tray menu. Settings switches animate their thumb and track color over 160 ms; closing settings releases the separate settings process.
