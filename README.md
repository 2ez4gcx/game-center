# Game Center

Launcher miễn phí cho game retro, chạy mọi hệ máy qua RetroArch. Thiết kế chi tiết: [GAME_CENTER_IMPLEMENTATION.md](GAME_CENTER_IMPLEMENTATION.md).

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/GameCenter.Core` | Scanner (extension, zip, header SEGA, CUE, nhiều đĩa → `.m3u`, `.bin` đơn → `.cue` tạm), SQLite, sinh `retroarch.cfg`, khởi chạy emulator, BIOS, sao lưu |
| `src/GameCenter.App` | Giao diện WPF chữ lớn, điều khiển bằng tay cầm XInput |
| `tests/GameCenter.Tests` | Kiểm thử scanner / database / cấu hình |
| `Defaults/` | `platforms.json` (core từng hệ máy), `retroarch-base.cfg` |
| `Licenses/` | Giấy phép (file placeholder — thay bằng bản đúng phiên bản khi đóng gói) |
| `installer/GameCenter.iss` | Bộ cài Inno Setup, cài theo người dùng, không cần admin |
| `tools/fetch-retroarch.ps1` | Tải RetroArch + core + LICENSE vào thư mục publish |

## Build

Cần .NET 8 SDK.

```
dotnet test
dotnet run --project src/GameCenter.App
```

Chạy với thư mục dữ liệu riêng (kiểm thử): đặt biến môi trường `GAMECENTER_DATA_DIR`.

Phát hành:

```
./build.ps1
./tools/fetch-retroarch.ps1 -Version <phiên bản RetroArch>
ISCC installer/GameCenter.iss
```

## Core đã chọn (RetroArch 1.22.2)

| Hệ máy | Core | Giấy phép |
|---|---|---|
| NES | FCEUmm | GPLv2 |
| SNES | Snes9x | Phi thương mại |
| GB / GBC | Gambatte | GPLv2 |
| GBA | mGBA | MPL 2.0 |
| Mega Drive | Genesis Plus GX | Phi thương mại |
| PS1 | Beetle PSX HW | GPLv2 |

Bộ cài: `publish/GameCenter-Setup-0.1.0.exe`.

## Việc còn phải làm

- Chơi thử game thật trên từng hệ máy (đổi đĩa `.m3u`, `.bin` đơn PS1, thoát bằng START+SELECT).
- Chọn giấy phép cho mã nguồn Game Center (`Licenses/GameCenter-LICENSE.txt`).
- Có thể thu gọn bộ cài: bỏ bớt `assets`/`shaders`/`overlays` không dùng của RetroArch.
- Nhận diện theo hash với DAT No-Intro/Redump.
