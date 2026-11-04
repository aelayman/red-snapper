# Builds installer\Output\RedSnapper.zip: the app plus Install.cmd, for PCs that block unsigned .exe installers.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$stage = Join-Path $here 'publish-zip'
$app = Join-Path $stage 'RedSnapper\app'
$zip = Join-Path $here 'Output\RedSnapper.zip'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
& dotnet publish (Join-Path $root 'RedSnapper.csproj') -c Release -r win-x64 --self-contained false -o $app
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Remove-Item (Join-Path $app 'RedSnapper.exe'), (Join-Path $app '*.pdb') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'app.ico') $app
Copy-Item (Join-Path $here 'zip\*') (Join-Path $stage 'RedSnapper')

New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage 'RedSnapper') -DestinationPath $zip
Write-Host "Built $zip"
