param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'Programs\Red Snapper'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Red Snapper.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Red Snapper.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Stop-RedSnapper {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -like '*Programs\Red Snapper*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Milliseconds 800
}

if ($Uninstall) {
    Stop-RedSnapper
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $shortcut, $desktopShortcut -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $runKey -Name 'Red Snapper' -ErrorAction SilentlyContinue
    Write-Host 'Red Snapper has been removed. Your screenshots were left in place.'
    return
}

$src = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $src 'RedSnapper.dll'))) {
    throw "Can't find the 'app' folder. Right-click the zip, choose Extract All, and run Install again from the extracted folder."
}

$runtime = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
if (-not (Test-Path $dotnet) -or -not (Get-ChildItem $runtime -Directory -Filter '8.*' -ErrorAction SilentlyContinue)) {
    Write-Host 'Red Snapper needs the .NET 8 Desktop Runtime (x64), which is not installed on this PC.'
    Write-Host 'Install it from https://dotnet.microsoft.com/download/dotnet/8.0 (or ask IT), then run Install again.'
    Start-Process 'https://dotnet.microsoft.com/download/dotnet/8.0'
    return
}

Stop-RedSnapper
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $src '*') $dest -Recurse -Force
Get-ChildItem $dest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

$dll = Join-Path $dest 'RedSnapper.dll'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $dotnet
$lnk.Arguments = "`"$dll`""
$lnk.WorkingDirectory = $dest
$lnk.IconLocation = Join-Path $dest 'app.ico'
$lnk.Description = 'Red Snapper screenshots'
$lnk.Save()

if ((Read-Host 'Start Red Snapper when you sign in to Windows? (Y/n)') -notmatch '^[nN]') {
    Set-ItemProperty -Path $runKey -Name 'Red Snapper' -Value "`"$dotnet`" `"$dll`""
}
if ((Read-Host 'Add a shortcut to your desktop? (y/N)') -match '^[yY]') {
    $d = $shell.CreateShortcut($desktopShortcut)
    $d.TargetPath = $dotnet
    $d.Arguments = "`"$dll`""
    $d.WorkingDirectory = $dest
    $d.IconLocation = Join-Path $dest 'app.ico'
    $d.Save()
}

Start-Process $shortcut
Write-Host ''
Write-Host 'Done! Look for the little red fish in the system tray (bottom-right, near the clock),'
Write-Host 'then press Print Screen to take a screenshot.'
