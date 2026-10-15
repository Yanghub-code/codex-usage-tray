[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsDirectory = Join-Path $repoRoot 'artifacts'
$binDirectory = Join-Path $artifactsDirectory 'bin'
$packageName = "CodexUsageTray-v$Version-win-x64"
$packageDirectory = Join-Path $artifactsDirectory $packageName
$zipPath = Join-Path $artifactsDirectory "$packageName.zip"
$checksumPath = "$zipPath.sha256"

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -OutputDirectory $binDirectory

$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsDirectory).TrimEnd('\') + '\'
$resolvedPackage = [System.IO.Path]::GetFullPath($packageDirectory)
if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to package outside the artifacts directory.'
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $binDirectory 'CodexUsageTray.exe') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $packageDirectory

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText($checksumPath, "$hash  $([System.IO.Path]::GetFileName($zipPath))`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Packaged $zipPath"
Write-Host "SHA-256 $hash"
