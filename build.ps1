param([switch]$Publish, [switch]$Test)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
Push-Location $PSScriptRoot
try {
    & $taskDotnet restore ErebusGG.sln --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & $taskDotnet build ErebusGG.sln -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    if ($Test) {
        & $taskDotnet run --project tests\EternalGG.Tests -c Release --no-build
        if ($LASTEXITCODE) { throw 'Tests failed' }
        & $taskDotnet run --project tests\EternalGG.UiTests -c Release --no-build
        if ($LASTEXITCODE) { throw 'UI checks failed' }
    }
    if ($Publish) {
        & $taskDotnet publish src\EternalGG.App -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o artifacts\erebusgg-win-x64
        if ($LASTEXITCODE) { throw 'Publish failed' }
    }
}
finally { Pop-Location }
