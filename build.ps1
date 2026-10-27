param([string]$OutputPath = (Join-Path $PSScriptRoot 'bin\LoyGoldPrice.exe'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$OutputPath" "/win32icon:$root\assets\gold.ico" "/resource:$root\assets\gold.ico,LoyGoldPrice.Gold.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" (Join-Path $root 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Host "已生成 $OutputPath"
