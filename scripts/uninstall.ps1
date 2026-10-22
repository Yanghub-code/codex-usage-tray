[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$localAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'CodexUsageTray'))
$expectedDirectory = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'CodexUsageTray'))
if ($installDirectory -ne $expectedDirectory -or $installDirectory -eq $localAppData) {
    throw 'Refusing to remove an unexpected installation directory.'
}

$targetExecutable = Join-Path $installDirectory 'CodexUsageTray.exe'
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Tray.lnk'
$watcherShortcutPath = Join-Path $startupDirectory 'Codex Usage Tray Watcher.lnk'

$running = Get-CimInstance Win32_Process -Filter "Name='CodexUsageTray.exe'" |
    Where-Object { $_.ExecutablePath -eq $targetExecutable }
foreach ($process in $running) {
    Stop-Process -Id $process.ProcessId -Force
}

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}
if (Test-Path -LiteralPath $watcherShortcutPath) {
    Remove-Item -LiteralPath $watcherShortcutPath -Force
}
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Host 'Codex Usage Tray was removed.'

