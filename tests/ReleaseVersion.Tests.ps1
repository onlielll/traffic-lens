$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$workflow = Get-Content -LiteralPath (Join-Path $taskRoot '.github\workflows\release.yml') -Raw
$version = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'native\Native.csproj') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$guardMatch = [regex]::Match($workflow, '(?ms)- name: Verify release tag matches source version.*?run: \|\r?\n(?<body>.*?)(?=\r?\n      - uses:)')
if (-not $guardMatch.Success) { throw 'Release tag check not found.' }
$guard = [scriptblock]::Create([regex]::Replace($guardMatch.Groups['body'].Value, '(?m)^          ', ''))
$previousTag = $env:RELEASE_TAG
Push-Location $taskRoot
try {
    $env:RELEASE_TAG = 'v' + $version
    & $guard
    $env:RELEASE_TAG = 'v0.0.0'
    $rejected = $false
    try { & $guard } catch { $rejected = $_.Exception.Message -like 'Release tag must match*' }
    if (-not $rejected) { throw 'Mismatched release tag was not rejected.' }
    $packageMatch = [regex]::Match($workflow, '\./scripts/package\.ps1[^\r\n]* -Version (?<value>.*?) -Compiler ')
    if (-not $packageMatch.Success) { throw 'Package version argument not found.' }
    $project = [xml](Get-Content native/Native.csproj -Raw)
    $packageVersion = & ([scriptblock]::Create($packageMatch.Groups['value'].Value))
    if ($packageVersion -isnot [string] -or $packageVersion -cne $version) {
        throw 'Workflow must pass the scalar project version to packaging.'
    }
    Write-Output 'PASS: matching tag accepted, mismatched tag rejected, scalar package version preserved.'
} finally {
    Pop-Location
    $env:RELEASE_TAG = $previousTag
}
