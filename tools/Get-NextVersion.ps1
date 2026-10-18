param([string]$Current)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Current)) {
    $versionProps = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)
    $Current = [string]$versionProps.Project.PropertyGroup.Version
}
if ($Current -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]?)\.(0|[1-9][0-9]?)$') {
    throw 'Version must be major.minor.patch with minor and patch between 0 and 99.'
}
$versionParts = $Current.Split('.')
$versionMajor = [long]$versionParts[0]
$versionMinor = [int]$versionParts[1]
$versionPatch = [int]$versionParts[2] + 1
if ($versionPatch -eq 100) { $versionPatch = 0; $versionMinor++ }
if ($versionMinor -eq 100) { $versionMinor = 0; $versionMajor++ }
Write-Output "$versionMajor.$versionMinor.$versionPatch"
