[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\plugin-packages'),
    [switch]$InstallTemplates
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$templateProject = Join-Path $PSScriptRoot 'Templates\Drive.Plugin.Templates.csproj'
$packageVersion = (& dotnet msbuild $templateProject -getProperty:PackageVersion -nologo | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($packageVersion)) { throw '无法读取模板包版本。' }
$sdkVersion = (& dotnet msbuild (Join-Path $PSScriptRoot 'Drive.Plugin.SDK\Drive.Plugin.SDK.csproj') -getProperty:PackageVersion -nologo | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) { throw '无法读取 SDK 包版本。' }
# 模板可以独立发布修订版；生成项目的引用仍须匹配实际 SDK 版本。
$templateXml = [xml](Get-Content (Join-Path $PSScriptRoot 'Templates\drive-plugin\DrivePluginApp.csproj') -Raw)
foreach ($reference in $templateXml.Project.ItemGroup.PackageReference) {
    if ($reference.Include -like 'Drive.Plugin.*' -and $reference.Version -ne $sdkVersion) {
        throw "模板中的 $($reference.Include) 版本 $($reference.Version) 与 SDK 版本 $sdkVersion 不一致。"
    }
}
foreach ($name in @('Drive.Plugin.Abi', 'Drive.Plugin.SDK', 'Drive.Plugin.Avalonia')) {
    & dotnet pack (Join-Path $PSScriptRoot "$name\$name.csproj") -c Release -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw "Failed to pack $name" }
}
& dotnet pack $templateProject -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Failed to pack templates' }
if ($InstallTemplates) {
    # 从本地包升级时 --force 可能并存多个版本；先卸载本平台的旧模板，避免身份冲突。
    $installedTemplates = (& dotnet new uninstall | Out-String)
    if ($LASTEXITCODE -ne 0) { throw '无法读取已安装的开发模板。' }
    if ($installedTemplates -match '(?m)^\s+Drive\.Plugin\.Templates\s*$') {
        & dotnet new uninstall Drive.Plugin.Templates
        if ($LASTEXITCODE -ne 0) { throw '旧版开发模板卸载失败。' }
    }
    & dotnet new install (Join-Path $output "Drive.Plugin.Templates.$packageVersion.nupkg") --force
    if ($LASTEXITCODE -ne 0) { throw '开发模板安装失败。' }
}
Write-Host "Local SDK and templates: $output"
