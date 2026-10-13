param(
    [ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$Test,
    [switch]$Development,
    [switch]$Verification
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$localSdk = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }

if ($Test) {
    & $dotnet run --project (Join-Path $taskRoot 'tests\Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
}

$buildRoot = if ($Verification) { Join-Path $taskRoot '.build\verification' } else { Join-Path $taskRoot '.build' }
$nativeOutput = Join-Path $buildRoot "native-$Runtime"
$settingsOutput = Join-Path $buildRoot "settings-$Runtime"
$mergedOutput = Join-Path $buildRoot "publish\$Runtime"
$sharedArgs = @('-c','Release','--nologo','-r',$Runtime,'--self-contained',(-not $Development).ToString().ToLowerInvariant(),'-p:DebugType=none','-p:DebugSymbols=false','-p:PublishReadyToRun=false')
if ($Verification) { $sharedArgs += '-p:VerificationBuild=true' }
& $dotnet publish (Join-Path $taskRoot 'native\Native.csproj') @sharedArgs -o $nativeOutput
if ($LASTEXITCODE -ne 0) { throw 'Resident build failed' }
& $dotnet publish (Join-Path $taskRoot 'src\ProxyTrafficFloat.csproj') @sharedArgs -o $settingsOutput
if ($LASTEXITCODE -ne 0) { throw 'Settings build failed' }

# Both processes share one runtime. WPF WindowsBase replaces the Core runtime type-forwarding stub.
New-Item -ItemType Directory -Force $mergedOutput | Out-Null
Copy-Item (Join-Path $nativeOutput '*') -Destination $mergedOutput -Force
foreach ($file in Get-ChildItem -LiteralPath $settingsOutput -File) {
    $existing = Join-Path $mergedOutput $file.Name
    if ((Test-Path -LiteralPath $existing) -and $file.Name -notlike 'ProxyTraffic*' -and $file.Name -ne 'WindowsBase.dll') {
        if ((Get-FileHash -LiteralPath $existing).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Runtime file version mismatch: $($file.Name)"
        }
    }
    Copy-Item -LiteralPath $file.FullName -Destination $mergedOutput -Force
}
if (Test-Path -LiteralPath (Join-Path $taskRoot 'LICENSE')) { Copy-Item (Join-Path $taskRoot 'LICENSE') $mergedOutput -Force }
Write-Host "Built: $mergedOutput"
