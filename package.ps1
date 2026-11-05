param([string]$ExecutablePath = (Join-Path $PSScriptRoot 'bin\LoyGoldPrice.exe'))
$ErrorActionPreference = 'Stop'
$exe = $ExecutablePath
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw '请先执行 ./build.ps1'
}

$output = Join-Path $PSScriptRoot 'work\package'
New-Item -ItemType Directory -Force $output | Out-Null
$zip = Join-Path $output 'LoyGoldPrice-windows-x64.zip'
Compress-Archive -LiteralPath $exe, (Join-Path $PSScriptRoot 'README.md') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Value "$hash  LoyGoldPrice-windows-x64.zip" -Encoding ascii
Write-Host "已生成 $zip"
