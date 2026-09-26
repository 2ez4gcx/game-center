<#
  Build bản phát hành vào publish/GameCenter (self-contained, không cần cài .NET trên máy người dùng).
  Sau đó: tools/fetch-retroarch.ps1 -Version <x.y.z>  →  ISCC installer/GameCenter.iss
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'publish/GameCenter'
dotnet test (Join-Path $PSScriptRoot 'tests/GameCenter.Tests')
dotnet publish (Join-Path $PSScriptRoot 'src/GameCenter.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $out
Write-Host "Đã build vào $out"
