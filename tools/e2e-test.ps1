<#
.SYNOPSIS
  Kiểm thử đầu-cuối với RetroArch và core thật (docs/bao-toan-save.md, mục 4.2). Chạy trước mỗi lần phát hành.

.DESCRIPTION
  Dùng RetroArch + core trong thư mục cài Game Center (hoặc thư mục publish sau khi chạy fetch-retroarch.ps1).
  Ảnh đĩa PS1 và ROM Game Boy là file giả tự tạo, không cần game thật. Mỗi kiểm thử mở cửa sổ RetroArch vài giây.
  Log RetroArch được giữ ở thư mục -LogDir để xem lại.

.EXAMPLE
  ./tools/e2e-test.ps1 -AppDir C:\GameCenter
  ./tools/e2e-test.ps1 -AppDir ./publish/GameCenter
#>
param(
    [Parameter(Mandatory)] [string] $AppDir,
    [string] $LogDir = "$PSScriptRoot/../publish/e2e-logs"
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $AppDir 'Emulators'))) { throw "Không thấy thư mục Emulators trong $AppDir" }
$env:GAMECENTER_E2E_APPDIR = (Resolve-Path $AppDir).Path
$env:GAMECENTER_E2E_LOGDIR = $LogDir
dotnet test (Join-Path $PSScriptRoot '../tests/GameCenter.Tests') --filter 'FullyQualifiedName~E2E' --logger 'console;verbosity=normal'
if ($LASTEXITCODE -ne 0) { throw 'Kiểm thử đầu-cuối thất bại' }
Write-Host "Xong. Log RetroArch: $LogDir"
