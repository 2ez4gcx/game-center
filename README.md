# Game Center

Launcher **miễn phí** cho game retro trên Windows, thiết kế cho người lớn tuổi: chữ lớn, nút to, điều khiển bằng tay cầm hoặc bàn phím. Mọi hệ máy chạy qua RetroArch.

```text
Cài đặt → Next → Finish → Chép game → Chơi
```

## Tải về

Tải `GameCenter-Setup-x.y.z.exe` ở mục [Releases](https://github.com/2ez4gcx/game-center/releases). Bộ cài đã kèm sẵn .NET 8, RetroArch và các core giả lập, không cần cài thêm gì.

- Không cần quyền admin. Mặc định cài vào `C:\GameCenter` (có thể chọn ổ khác).
- Game, save, BIOS nằm ngay trong thư mục cài đặt: `Games\`, `Saves\`, `States\`, `BIOS\`.
- Gỡ cài đặt **không xóa** game và save. Cài lại vào cùng chỗ là dùng tiếp.
- Bộ cài **không kèm game hay BIOS**.

## Hệ máy hỗ trợ

| Hệ máy | Định dạng | Core | Giấy phép core |
|---|---|---|---|
| NES | `.nes`, `.zip` | FCEUmm | GPLv2 |
| SNES | `.sfc`, `.smc`, `.zip` | Snes9x | Phi thương mại |
| Game Boy / Color | `.gb`, `.gbc`, `.zip` | Gambatte | GPLv2 |
| GBA | `.gba`, `.zip` | mGBA | MPL 2.0 |
| Mega Drive | `.md`, `.gen`, `.bin`, `.zip` | Genesis Plus GX | Phi thương mại |
| PS1 | `.cue`, `.bin`, `.pbp`, `.chd`, `.m3u` | Beetle PSX HW | GPLv2 |

PS1 chơi được ngay không cần BIOS (OpenBIOS). Thêm BIOS bạn tự sao lưu vào thư mục `BIOS\` để tương thích tốt hơn.

## Sử dụng

**Thêm game:** chép vào thư mục `Games\`, để chung một chỗ cũng được. Game Center tự nhận ra hệ máy theo nội dung file và tự hiện game mới sau vài giây.

- `.cue` + `.bin` PS1 hiện một lần; game nhiều đĩa tự ghép và đổi đĩa được khi đang chơi.
- `.bin` không có `.cue`: nhận đĩa PS1 hoặc ROM Mega Drive theo nội dung.
- `.zip` nhiều ROM: mỗi ROM thành một game.
- File không nhận ra được nằm ở mục **Chưa nhận ra**, bấm để chọn hệ máy.

**Điều khiển:** chọn **Tay cầm** hoặc **Bàn phím** ở góc trái; bàn phím luôn dùng được song song với tay cầm.

| Tay cầm | Bàn phím (mặc định) |
|---|---|
| D-pad ↑ ← ↓ → | W A S D |
| X / Y / B / A | I / J / K / L |
| L1 / L2 | Q / E |
| R1 / R2 | U / O |
| START / SELECT | Enter / Space |

Đổi phím: **Thiết lập phím** (bấm vào ô rồi nhấn phím mới).

**Thoát game:** giữ **START + SELECT** trên tay cầm, hoặc bấm **Esc**. **F1** mở menu giả lập (đổi đĩa…), **F2 / F4** lưu / tải nhanh.

**Save game:**
- Save trong game (memory card, save trong băng) luôn được giữ, tự ghi xuống ổ mỗi 10 giây.
- Khi thoát, Game Center hỏi có **lưu lại chỗ đang chơi** không; lần mở sau hỏi **chơi tiếp** hay **chơi từ đầu**. Tắt được trong Cài đặt.
- **Cài đặt → Sao lưu dữ liệu** nén toàn bộ save thành file zip có ngày.

**Điều khiển launcher bằng tay cầm:** ↑↓ chọn game, A chơi, LB/RB đổi mục, Y yêu thích, X thêm tùy chọn, START cài đặt, SELECT trợ giúp.

## Phát triển

Cần .NET 8 SDK.

```
dotnet test
dotnet run --project src/GameCenter.App
```

Chạy với thư mục dữ liệu riêng khi kiểm thử: đặt biến môi trường `GAMECENTER_DATA_DIR`.

| Thư mục | Nội dung |
|---|---|
| `src/GameCenter.Core` | Quét và nhận diện game, SQLite, sinh `retroarch.cfg`, sơ đồ phím, save state, khởi chạy RetroArch |
| `src/GameCenter.App` | Giao diện WPF, điều khiển bằng tay cầm XInput |
| `tests/GameCenter.Tests` | Kiểm thử |
| `Defaults/` | `platforms.json` (core từng hệ máy), `retroarch-base.cfg` |
| `Licenses/` | Giấy phép Game Center, RetroArch và từng core |
| `installer/` | Bộ cài Inno Setup 7 |
| `tools/fetch-retroarch.ps1` | Tải RetroArch, core và LICENSE vào thư mục publish |

Phát hành:

```
./build.ps1                                                   # test + publish self-contained (kèm .NET 8)
./tools/fetch-retroarch.ps1 -Version 1.22.2                   # RetroArch + core + LICENSE
& "C:\Program Files\Inno Setup 7\ISCC.exe" installer\GameCenter.iss
```

Thiết kế chi tiết: [GAME_CENTER_IMPLEMENTATION.md](GAME_CENTER_IMPLEMENTATION.md).

## Giấy phép

Game Center miễn phí, không quảng cáo, không tính năng trả phí. RetroArch và các core giữ giấy phép riêng (xem `Licenses/`). Snes9x và Genesis Plus GX chỉ cho phép phân phối phi thương mại. Game Center không phải sản phẩm chính thức của Sony, Nintendo hay Sega.
