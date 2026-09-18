[CmdletBinding()]
param([string]$Runtime = '', [string]$PluginDirectory = (Join-Path $PSScriptRoot '../artifacts/plugins-managed'))
$ErrorActionPreference = 'Stop'
$output = Join-Path ([IO.Path]::GetFullPath($PluginDirectory)) 'MusicPlugin'
$publishArguments = @('publish', (Join-Path $PSScriptRoot 'Examples/MusicPlugin/MusicPlugin.csproj'), '-c', 'Release', '--self-contained', 'false', '-o', $output, '--nologo')
if ($Runtime) { $publishArguments += @('-r', $Runtime) }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Music plugin publish failed' }
Write-Host "Copy the complete MusicPlugin folder into the client Plugins directory: $output"
