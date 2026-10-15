[CmdletBinding()]
param(
    [switch]$NoStartup,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$sourceExecutable = Join-Path $PSScriptRoot 'CodexUsageTray.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw 'CodexUsageTray.exe must be in the same directory as install.ps1.'
}

$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexUsageTray'))
$targetExecutable = Join-Path $installDirectory 'CodexUsageTray.exe'
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Tray.lnk'

$running = Get-CimInstance Win32_Process -Filter "Name='CodexUsageTray.exe'" |
    Where-Object { $_.ExecutablePath -eq $targetExecutable }
foreach ($process in $running) {
    Stop-Process -Id $process.ProcessId -Force
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceExecutable -Destination $targetExecutable -Force

if (-not $NoStartup) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetExecutable
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = 'Codex quota and memory taskbar meters'
    $shortcut.Save()
}

if (-not $NoStart) {
    Start-Process -FilePath $targetExecutable -WorkingDirectory $installDirectory
}

Write-Host "Installed Codex Usage Tray to $installDirectory"
