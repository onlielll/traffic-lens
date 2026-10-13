param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Version = '2.1.0',
    [string]$Compiler,
    [switch]$IncludeSource,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPublished = Join-Path $taskRoot ".build\publish\$Runtime"
$taskOutput = Join-Path $taskRoot 'dist'
$taskName = "TrafficLens-$Version-$Runtime"
$taskProject = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'native\Native.csproj') -Raw)
if ($taskProject.Project.PropertyGroup.Version -ne $Version) {
    throw "Package version $Version differs from native\Native.csproj. Keep binaries, tags and package names aligned."
}

# Validate the complete merged, self-contained folder before publishing a package.
foreach ($taskFile in @('ProxyTrafficFloat.exe', 'ProxyTrafficSettings.exe',
    'coreclr.dll', 'hostfxr.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskPublished $taskFile))) {
        throw "Incomplete publish output: $taskFile. Run build.ps1 -Runtime $Runtime first."
    }
}
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
foreach ($taskDocument in @('README.md', 'README.en.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-DOTNET.txt', 'THIRD-PARTY-NOTICES-RUNTIME.txt', 'THIRD-PARTY-NOTICES-HEROICONS.txt', 'THIRD-PARTY-NOTICES-OXYPLOT.txt')) {
    $taskDocumentPath = Join-Path $taskRoot $taskDocument
    if (Test-Path -LiteralPath $taskDocumentPath) {
        Copy-Item -LiteralPath $taskDocumentPath -Destination $taskPublished -Force
    }
}
$taskDocs = Join-Path $taskPublished 'docs'
New-Item -ItemType Directory -Force -Path $taskDocs | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\VALIDATION.md') -Destination $taskDocs -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\images') -Destination $taskDocs -Recurse -Force
foreach ($taskInternalDocument in @('FIRST-GITHUB-UPLOAD.md', 'LICENSE-OPTIONS.md', 'REPOSITORY-INTRO.md')) {
    $taskStaleDocument = Join-Path $taskDocs $taskInternalDocument
    if (Test-Path -LiteralPath $taskStaleDocument) { Remove-Item -LiteralPath $taskStaleDocument -Force }
}
Compress-Archive -Path (Join-Path $taskPublished '*') -DestinationPath (Join-Path $taskOutput "$taskName-portable.zip") -CompressionLevel Optimal -Force
$taskPackages = @("$taskName-portable.zip")

if (-not $SkipInstaller) {
    if (-not $Compiler) {
        $taskCandidates = @(
            (Join-Path $taskRoot '.tools\inno\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        )
        $Compiler = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler)) {
        throw 'Install Inno Setup 6, provide -Compiler, or use -SkipInstaller for a ZIP-only build.'
    }
    & $Compiler "/DAppVersion=$Version" "/DRuntime=$Runtime" "/DSourcePath=$taskPublished" "/DOutputPath=$taskOutput" (Join-Path $taskRoot 'installer\TrafficLens.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)." }
    $taskPackages += "$taskName-setup.exe"
}

if ($IncludeSource) {
    $taskSourceOutput = [IO.Path]::GetFullPath((Join-Path $taskOutput 'GitHub-Source'))
    $taskOutputBoundary = [IO.Path]::GetFullPath($taskOutput) + [IO.Path]::DirectorySeparatorChar
    if (-not $taskSourceOutput.StartsWith($taskOutputBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Source output must stay inside dist.'
    }
    if (Test-Path -LiteralPath $taskSourceOutput) { Remove-Item -LiteralPath $taskSourceOutput -Recurse -Force }
    New-Item -ItemType Directory -Path $taskSourceOutput | Out-Null
    # Allowlist the public source tree; never copy the entire development workspace.
    $taskDirectories = @('native', 'src', 'tests', 'assets', 'docs', 'installer', 'scripts', '.github')
    $taskExtensions = @('.cs', '.csproj', '.xaml', '.manifest', '.ico', '.png', '.svg', '.bin', '.ps1', '.iss', '.yml', '.yaml', '.md', '.txt')
    foreach ($taskDirectory in $taskDirectories) {
        $taskDirectoryPath = Join-Path $taskRoot $taskDirectory
        if (-not (Test-Path -LiteralPath $taskDirectoryPath)) { continue }
        Get-ChildItem -LiteralPath $taskDirectoryPath -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and ($taskExtensions -contains $_.Extension.ToLowerInvariant() -or $_.Name -eq 'LICENSE')
        } | ForEach-Object {
            $taskRelative = $_.FullName.Substring($taskRoot.Length + 1)
            $taskDestination = Join-Path $taskSourceOutput $taskRelative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskDestination) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $taskDestination
        }
    }
    foreach ($taskFile in @('README.md', 'README.en.md', 'CONTRIBUTING.md', 'CHANGELOG.md',
        'LICENSE', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-DOTNET.txt', 'THIRD-PARTY-NOTICES-RUNTIME.txt', 'THIRD-PARTY-NOTICES-HEROICONS.txt', 'THIRD-PARTY-NOTICES-OXYPLOT.txt', 'build.ps1', 'global.json', '.editorconfig', '.gitignore', '.gitattributes')) {
        $taskFilePath = Join-Path $taskRoot $taskFile
        if (Test-Path -LiteralPath $taskFilePath) { Copy-Item -LiteralPath $taskFilePath -Destination $taskSourceOutput }
    }
    $taskSourcePackage = "TrafficLens-$Version-source.zip"
    Compress-Archive -Path $taskSourceOutput -DestinationPath (Join-Path $taskOutput $taskSourcePackage) -CompressionLevel Optimal -Force
    $taskPackages += $taskSourcePackage
}

$taskChecksums = foreach ($taskPackage in $taskPackages) {
    $taskHash = Get-FileHash -LiteralPath (Join-Path $taskOutput $taskPackage) -Algorithm SHA256
    '{0}  {1}' -f $taskHash.Hash.ToLowerInvariant(), $taskPackage
}
$taskChecksums | Set-Content -LiteralPath (Join-Path $taskOutput "$taskName-SHA256SUMS.txt") -Encoding ascii
Write-Host "Packages ready: $taskOutput"
