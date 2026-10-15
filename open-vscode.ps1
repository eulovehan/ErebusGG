$ErrorActionPreference = 'Stop'
$taskDotnetRoot = Join-Path $PSScriptRoot '.tools\dotnet'
if (!(Test-Path -LiteralPath (Join-Path $taskDotnetRoot 'dotnet.exe'))) {
    & (Join-Path $PSScriptRoot 'bootstrap.ps1')
}
if (Get-Process Code -ErrorAction SilentlyContinue) {
    throw 'Close all VS Code windows before launching so the project SDK environment can be applied.'
}

$env:DOTNET_ROOT = $taskDotnetRoot
$env:PATH = $taskDotnetRoot + [IO.Path]::PathSeparator + $env:PATH
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\nuget'
$taskCode = Get-Command code -ErrorAction Stop
& $taskCode.Source --new-window $PSScriptRoot
if ($LASTEXITCODE) { throw 'VS Code launch failed' }
