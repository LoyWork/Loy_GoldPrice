$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'work') | Out-Null
$exe = Join-Path $PSScriptRoot 'work\QuoteTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /main:QuoteTests /utf8output "/out:$exe" "/resource:$PSScriptRoot\assets\gold.ico,LoyGoldPrice.Gold.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" (Join-Path $PSScriptRoot 'src\Program.cs') (Join-Path $PSScriptRoot 'tests\QuoteTests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& $exe
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
