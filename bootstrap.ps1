$ErrorActionPreference = 'Stop'
$taskTools = Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskInstaller = Join-Path $taskTools 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller
& $taskInstaller -Version '10.0.402' -InstallDir (Join-Path $taskTools 'dotnet') -NoPath
if ($LASTEXITCODE) { throw 'SDK installation failed' }
