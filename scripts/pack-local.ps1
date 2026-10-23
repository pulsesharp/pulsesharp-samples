<#
Packs PulseSharp from a local checkout into .local-feed with a unique version, and makes the samples use it.
Needed only until PulseSharp 0.6.0-alpha is on nuget.org. Delete .local-feed to go back to the published version.

  ./scripts/pack-local.ps1 [-PulseSharpPath D:\Code\PulseSharp]
#>
param(
    [string]$PulseSharpPath = (Join-Path $PSScriptRoot '..\..\PulseSharp')
)

$ErrorActionPreference = 'Stop'
$feed = Join-Path $PSScriptRoot '..\.local-feed'
New-Item -ItemType Directory -Force $feed | Out-Null
$feed = (Resolve-Path $feed).Path
$version = '0.6.0-dev.' + (Get-Date -Format 'yyyyMMddHHmmss')

Push-Location $PulseSharpPath
try {
    dotnet pack -c Release -o $feed "-p:Version=$version" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }
}
finally {
    Pop-Location
}

$props = "<Project><PropertyGroup><PulseSharpVersion>$version</PulseSharpVersion></PropertyGroup></Project>"
Set-Content -Path (Join-Path $feed 'version.props') -Value $props -Encoding utf8
Write-Host "Packed PulseSharp $version into $feed"
