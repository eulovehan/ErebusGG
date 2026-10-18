param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$installerRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installerProps = [xml](Get-Content -LiteralPath (Join-Path $installerRoot 'Directory.Build.props') -Raw)
$installerVersion = [string]$installerProps.Project.PropertyGroup.Version
if ($installerVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]?)\.(0|[1-9][0-9]?)$') { throw 'Invalid application version' }
$installerPublish = Join-Path $installerRoot 'artifacts/erebusgg-win-x64'
if (!(Test-Path -LiteralPath (Join-Path $installerPublish 'ErebusGG.exe'))) { throw 'Run build.ps1 -Test -Publish first' }
if (!$Compiler) {
    $installerLocalCompiler = Join-Path $installerRoot '.tools/nsis/nsis-3.13/makensis.exe'
    $Compiler = if (Test-Path -LiteralPath $installerLocalCompiler) { $installerLocalCompiler } else { (Get-Command makensis -ErrorAction Stop).Source }
}
$installerOutputDir = Join-Path $installerRoot 'artifacts/releases'
[IO.Directory]::CreateDirectory($installerOutputDir) | Out-Null
$installerOutput = Join-Path $installerOutputDir "ErebusGG-Setup-$installerVersion.exe"
$installerManifest = Join-Path $installerOutputDir 'uninstall-files.nsh'
$installerManifestLines = [Collections.Generic.List[string]]::new()
foreach ($installerFile in Get-ChildItem -LiteralPath $installerPublish -Recurse -File | Where-Object Extension -ne '.pdb') {
    $installerRelative = [IO.Path]::GetRelativePath($installerPublish,$installerFile.FullName).Replace('$','$$')
    $installerManifestLines.Add('Delete "$INSTDIR\' + $installerRelative + '"')
}
foreach ($installerDirectory in Get-ChildItem -LiteralPath $installerPublish -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
    $installerRelative = [IO.Path]::GetRelativePath($installerPublish,$installerDirectory.FullName).Replace('$','$$')
    $installerManifestLines.Add('RMDir "$INSTDIR\' + $installerRelative + '"')
}
[IO.File]::WriteAllLines($installerManifest,$installerManifestLines,[Text.UTF8Encoding]::new($true))
& $Compiler /V2 "/DAPP_VERSION=$installerVersion" "/DPUBLISH_DIR=$installerPublish" "/DOUTPUT_FILE=$installerOutput" "/DUNINSTALL_MANIFEST=$installerManifest" (Join-Path $PSScriptRoot 'installer.nsi')
if ($LASTEXITCODE) { throw 'Installer compilation failed' }
Get-FileHash -LiteralPath $installerOutput -Algorithm SHA256 | Format-List
