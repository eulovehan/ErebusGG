$ErrorActionPreference = 'Stop'
$taskTools = Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskInstaller = Join-Path $taskTools 'dotnet-install.ps1'
$taskSdkVersion = (Get-Content (Join-Path $PSScriptRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller
& $taskInstaller -Version $taskSdkVersion -InstallDir (Join-Path $taskTools 'dotnet') -NoPath
if ($LASTEXITCODE) { throw 'SDK installation failed' }
