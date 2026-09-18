[CmdletBinding()]
param(
    [string]$Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "../artifacts/managed-desktop/$Runtime")
)
$ErrorActionPreference = 'Stop'
# 普通 .NET 自包含裁剪发布：Runner 自身裁剪，共享框架完整保留以支持外部插件。
& dotnet publish (Join-Path $PSScriptRoot 'Drive.Plugin.Runner/Drive.Plugin.Runner.csproj') -c Release -r $Runtime --self-contained true -p:PublishAot=false -p:PublishTrimmed=true -o ([IO.Path]::GetFullPath($OutputDirectory)) --nologo
if ($LASTEXITCODE -ne 0) { throw '插件运行器发布失败。' }
Write-Host "插件运行器已发布到：$OutputDirectory（与客户端主程序放在同一目录）"
