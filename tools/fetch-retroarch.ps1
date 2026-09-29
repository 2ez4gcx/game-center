<#
.SYNOPSIS
  Tải RetroArch (bản Windows x64) và các core đã chọn vào thư mục publish, kèm file LICENSE từng core.

.DESCRIPTION
  Chạy trên máy build, KHÔNG chạy trên máy người dùng. Sau khi chạy:
   - Kiểm tra lại tên file .dll khớp Defaults/platforms.json (mục 10.1).
   - Kiểm tra license từng core đúng phiên bản (mục 17) và điền Licenses/RetroArch-SOURCE.txt.
   - Không bao giờ thêm DuckStation, BIOS hoặc ROM.

.EXAMPLE
  ./tools/fetch-retroarch.ps1 -Version 1.19.1 -OutDir ./publish/GameCenter
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutDir = "$PSScriptRoot/../publish/GameCenter"
)
$ErrorActionPreference = 'Stop'

# Core → file LICENSE nguồn (raw GitHub). Cập nhật đường dẫn nếu repo đổi tên nhánh.
$cores = [ordered]@{
    'mednafen_psx_hw' = @{ License = 'https://raw.githubusercontent.com/libretro/beetle-psx-libretro/master/COPYING'; Name = 'beetle-psx-hw' }
    'snes9x'          = @{ License = 'https://raw.githubusercontent.com/libretro/snes9x/master/LICENSE';              Name = 'snes9x' }
    'mgba'            = @{ License = 'https://raw.githubusercontent.com/libretro/mgba/master/LICENSE';               Name = 'mgba' }
    'genesis_plus_gx' = @{ License = 'https://raw.githubusercontent.com/libretro/Genesis-Plus-GX/master/LICENSE.txt'; Name = 'genesis-plus-gx' }
    'fceumm'          = @{ License = 'https://raw.githubusercontent.com/libretro/libretro-fceumm/master/Copying';     Name = 'fceumm' }
    'gambatte'        = @{ License = 'https://raw.githubusercontent.com/libretro/gambatte-libretro/master/COPYING';  Name = 'gambatte' }
}

$sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if (-not $sevenZip) {
    # Dùng 7zr.exe chính thức từ 7-zip.org
    $sevenZipExe = Join-Path ([IO.Path]::GetTempPath()) '7zr.exe'
    if (-not (Test-Path $sevenZipExe)) { Invoke-WebRequest 'https://www.7-zip.org/a/7zr.exe' -OutFile $sevenZipExe -UseBasicParsing }
    $sevenZip = Get-Command $sevenZipExe
}

$work = Join-Path ([IO.Path]::GetTempPath()) "gc-retroarch-$Version"
New-Item -ItemType Directory -Force $work | Out-Null
$raDir = Join-Path $OutDir 'Emulators/RetroArch'
New-Item -ItemType Directory -Force (Join-Path $raDir 'cores') | Out-Null
$licDir = Join-Path $OutDir 'Licenses'
New-Item -ItemType Directory -Force (Join-Path $licDir 'Core-LICENSES') | Out-Null

# 1. RetroArch
$raUrl = "https://buildbot.libretro.com/stable/$Version/windows/x86_64/RetroArch.7z"
$raArchive = Join-Path $work 'RetroArch.7z'
Write-Host "Tải $raUrl"
Invoke-WebRequest $raUrl -OutFile $raArchive -UseBasicParsing
& $sevenZip.Source x $raArchive "-o$work/ra" -y | Out-Null
$raRoot = Get-ChildItem "$work/ra" -Recurse -Filter retroarch.exe | Select-Object -First 1
Copy-Item "$($raRoot.DirectoryName)/*" $raDir -Recurse -Force
Invoke-WebRequest "https://raw.githubusercontent.com/libretro/RetroArch/v$Version/COPYING" -OutFile (Join-Path $licDir 'RetroArch-LICENSE.txt') -UseBasicParsing

# 2. Core (bản stable chỉ có gói tất cả core; lấy từng core từ nightly/latest)
foreach ($core in $cores.Keys) {
    $zipUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/${core}_libretro.dll.zip"
    $zip = Join-Path $work "$core.zip"
    Write-Host "Tải $zipUrl"
    Invoke-WebRequest $zipUrl -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath (Join-Path $raDir 'cores') -Force
    Invoke-WebRequest $cores[$core].License -OutFile (Join-Path $licDir "Core-LICENSES/$($cores[$core].Name).txt") -UseBasicParsing
}

# 3. Kiểm tra: không có chương trình DuckStation (file chạy / thư viện).
#    RetroArch tự kèm vài file mô tả có chữ "duckstation" (info/duckstation_libretro.info, shader .params) — không tính.
$duck = Get-ChildItem $OutDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*duckstation*' -and $_.Extension -in '.exe', '.dll', '.so', '.dylib', '.appimage' }
if ($duck) {
    throw 'Phát hiện DuckStation trong thư mục publish — không được đóng gói (mục 2, 17).'
}

Write-Host "Xong. Hãy điền phiên bản $Version và commit mã nguồn vào Licenses/RetroArch-SOURCE.txt."
