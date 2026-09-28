<#
  Build bản phát hành Windows vào publish/GameCenter (self-contained, không cần cài .NET trên máy người dùng).
  Sau đó: tools/fetch-retroarch.ps1 -Version <x.y.z>  →  ISCC installer/GameCenter.iss

  Linux / macOS: xem tools/package-linux.sh và tools/package-macos.sh (hoặc để GitHub Actions build).
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'publish/GameCenter'
dotnet test (Join-Path $PSScriptRoot 'tests/GameCenter.Tests')
if ($LASTEXITCODE -ne 0) { throw 'Kiểm thử thất bại' }
dotnet publish (Join-Path $PSScriptRoot 'src/GameCenter.Desktop') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $out
Write-Host "Đã build vào $out"
