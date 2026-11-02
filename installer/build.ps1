# Publishes the app and builds installer\Output\RedSnapper-Setup.exe (requires Inno Setup 6).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$publish = Join-Path $here 'publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish (Join-Path $root 'RedSnapper.csproj') -c Release -r win-x64 --self-contained false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc (Join-Path $here 'RedSnapper.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Write-Host "Built $(Join-Path $here 'Output\RedSnapper-Setup.exe')"
