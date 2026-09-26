# Game Center Retro — Tài liệu triển khai (bản sửa đổi)

> Thay đổi so với bản trước:
> - Bỏ DuckStation khỏi bộ cài (giấy phép CC BY-NC-ND 4.0 không cho đóng gói lại). PS1 chuyển sang core **Beetle PSX HW** của RetroArch.
> - Toàn bộ platform chạy qua **một emulator duy nhất là RetroArch** để thống nhất tay cầm, save và cách thoát game.
> - Game PS1 nhiều đĩa dùng playlist `.m3u` để đổi đĩa khi đang chơi.
> - Tách thư mục ứng dụng và thư mục dữ liệu người dùng.
> - Bổ sung: phân loại `.zip` theo nội dung, nhận diện header Mega Drive, nhận diện theo hash, tổ hợp phím thoát game, điều khiển launcher bằng tay cầm, làm sạch tên game.
> - Bổ sung chính sách giấy phép từng core và chính sách donate.

## 1. Mục tiêu

Xây dựng một ứng dụng Windows **miễn phí** giúp người dùng lớn tuổi chơi các game retro bằng quy trình đơn giản:

```text
Cài đặt → Next → Next → Finish → Chép game → Quét → Chơi
```

Ứng dụng không tự viết lại emulator. Game Center đóng vai trò là launcher/frontend:

- Quét và nhận diện game.
- Tự phân loại game theo platform.
- Gọi RetroArch với đúng core và tham số.
- Quản lý save, save state, tay cầm và nhiều đĩa.
- Giữ nguyên game PS1 dạng `.bin/.cue` để phục vụ game Việt hóa.

## 2. Phạm vi phiên bản đầu tiên

### Nên hỗ trợ trước

| Platform | Định dạng | Core RetroArch | Giấy phép core |
|---|---|---|---|
| NES | `.nes`, `.zip` | Cần chọn (xem mục 17) | Cần kiểm tra |
| SNES | `.sfc`, `.smc`, `.zip` | Snes9x | Phi thương mại |
| Game Boy / Color | `.gb`, `.gbc`, `.zip` | mGBA hoặc Gambatte | mGBA: MPL; Gambatte: cần kiểm tra |
| Game Boy Advance | `.gba`, `.zip` | mGBA | MPL |
| Sega Mega Drive | `.md`, `.gen`, `.bin`, `.zip` | Genesis Plus GX | Phi thương mại |
| PlayStation 1 | `.cue`, `.bin`, `.pbp`, `.chd`, `.m3u` | Beetle PSX HW | GPL2 |

Danh sách định dạng PS1 cần kiểm tra lại theo tài liệu của core Beetle PSX HW trước khi phát hành.

### Có thể bổ sung sau

- Nintendo 64.
- Arcade/Neo Geo (lưu ý: FBNeo là giấy phép phi thương mại).
- PC Engine.
- Nintendo DS.
- PSP.
- Dreamcast.
- PlayStation 2.

Không nên hỗ trợ tất cả platform trong bản đầu tiên. Mỗi platform mới làm tăng số lượng cấu hình, BIOS, lỗi tay cầm, giấy phép và vấn đề tương thích cần kiểm tra.

### Không đóng gói

- **DuckStation**: giấy phép hiện tại CC BY-NC-ND 4.0; README của dự án coi gói đóng sẵn và cấu hình cài sẵn là bản sửa đổi, và tác giả không cho bên khác cung cấp bản build. Người dùng vẫn có thể tự tải về và cài cho mục đích cá nhân (xem mục 10.3).

## 3. Kiến trúc tổng thể

```text
┌─────────────────────────────────────────┐
│              Game Center.exe             │
├─────────────────────────────────────────┤
│ Giao diện người dùng                     │
│ - Danh sách game, bộ lọc, tìm kiếm       │
│ - Điều khiển bằng chuột và tay cầm       │
├─────────────────────────────────────────┤
│ Game Scanner                             │
│ - Quét thư mục                           │
│ - Nhận diện extension, header, hash      │
│ - Mở .zip để xem nội dung                │
│ - Ghép CUE/BIN, sinh .m3u                │
│ - Kiểm tra file lỗi                      │
├─────────────────────────────────────────┤
│ Game Database (SQLite)                   │
├─────────────────────────────────────────┤
│ Emulator Manager                         │
│ - Chọn core                              │
│ - Tạo command line                       │
│ - Khởi chạy, theo dõi, hiện lại launcher │
├─────────────────────────────────────────┤
│ Settings / Saves / BIOS / Licenses       │
└─────────────────────────────────────────┘
```

## 4. Công nghệ đề xuất

- Ngôn ngữ: **C#**.
- Giao diện: **WPF** nếu chỉ nhắm đến Windows; Avalonia nếu sau này muốn hỗ trợ Linux.
- Database: **SQLite**.
- Đọc tay cầm cho launcher: XInput (tay cầm Xbox và tương thích).
- Bộ cài: **Inno Setup**.
- Logging: file `Logs/gamecenter.log` trong thư mục dữ liệu.
- Cấu hình: JSON cho thiết lập dễ đọc, SQLite cho danh sách game.
- Emulator: **RetroArch portable** trong thư mục ứng dụng.

## 5. Cấu trúc thư mục

Tách **ứng dụng** và **dữ liệu người dùng** để tránh lỗi quyền ghi và để gỡ/nâng cấp không ảnh hưởng game, save.

### 5.1. Thư mục ứng dụng

Chọn một trong hai cách, chốt từ đầu:

- **Cách A (khuyến nghị):** cài theo người dùng (per-user), không cần quyền admin, ví dụ `%LOCALAPPDATA%\Programs\GameCenter\`.
- **Cách B:** cài vào `C:\Program Files\GameCenter\` (cần admin), ứng dụng không ghi gì vào đây.

```text
GameCenter/                 (ứng dụng — chỉ đọc khi chạy)
├─ GameCenter.exe
├─ Emulators/
│  └─ RetroArch/
│     ├─ retroarch.exe
│     └─ cores/
├─ Defaults/
│  ├─ platforms.json
│  └─ retroarch-base.cfg
└─ Licenses/
```

### 5.2. Thư mục dữ liệu

Mặc định `%USERPROFILE%\GameCenter\` (dễ tìm hơn `AppData` với người lớn tuổi), cho phép đổi trong Cài đặt.

```text
GameCenter Data/
├─ Games/
│  ├─ NES/
│  ├─ SNES/
│  ├─ GB/
│  ├─ GBA/
│  ├─ MegaDrive/
│  └─ PS1/
├─ BIOS/
├─ Saves/
├─ States/
├─ Covers/
├─ Playlists/        (.m3u và .cue tạm do Game Center sinh ra)
├─ Config/
│  ├─ settings.json
│  └─ retroarch.cfg
├─ Database/
│  └─ games.db
└─ Logs/
```

## 6. Quy tắc đặt game

### Cách 1: đặt đúng thư mục platform

```text
Games/
├─ PS1/
│  └─ Final Fantasy VII/
└─ SNES/
   └─ Super Mario World.sfc
```

### Cách 2: chép ở bất kỳ thư mục con nào

Nút **Quét toàn bộ thư mục** quét đệ quy mọi thư mục bên dưới `Games/`.

## 7. Nhận diện game

### 7.1. Bảng extension

```json
{
  "NES": [".nes", ".zip"],
  "SNES": [".sfc", ".smc", ".zip"],
  "GB": [".gb", ".zip"],
  "GBC": [".gbc", ".zip"],
  "GBA": [".gba", ".zip"],
  "MegaDrive": [".md", ".gen", ".bin", ".zip"],
  "PS1": [".cue", ".bin", ".pbp", ".chd", ".m3u"]
}
```

### 7.2. Thứ tự ưu tiên

1. Thư mục platform nếu người dùng đã đặt đúng thư mục.
2. File `.zip`: mở ra, đọc extension file bên trong để phân loại. Nếu zip chứa nhiều ROM hoặc không nhận ra được → đánh dấu `scan_status = 'unknown'`.
3. Có file `.cue` cùng thư mục: ưu tiên PS1.
4. File `.pbp`, `.chd`: PS1.
5. File `.bin` không có `.cue`:
   - Kiểm tra header Mega Drive: chuỗi `SEGA` tại offset `0x100`. Có → Mega Drive.
   - Không có → coi là PS1 nghi ngờ (xem mục 8.4).
6. Vẫn không xác định được: hiện trong danh sách "Chưa nhận ra", cho người dùng chọn platform.

### 7.3. Nhận diện theo hash (Milestone 3–4)

- Tính CRC32/MD5 của ROM để khớp với cơ sở dữ liệu No-Intro/Redump → lấy tên chuẩn và ảnh bìa.
- Game Việt hóa sẽ không khớp hash → luôn dùng tên file làm dự phòng.

### 7.4. Làm sạch tên hiển thị

- Bỏ các phần như `(USA)`, `(Europe)`, `[!]`, `(Rev 1)`, `(Disc 1)` khi hiển thị.
- Cho phép đổi tên hiển thị trong app; **không đổi tên file thật**.

## 8. Xử lý PS1

### 8.1. Nguyên tắc

- Không chuyển `.bin/.cue` sang `.chd`.
- Không đổi tên `.bin` nếu chưa cập nhật nội dung `.cue`.
- Không hiển thị từng track `.bin` riêng lẻ.
- Nếu có `.cue`, hiển thị game một lần.
- Không bao giờ ghi file vào thư mục game của người dùng. File do Game Center sinh ra (`.m3u`, `.cue` tạm) nằm trong `Playlists/`.
- Nếu `.cue` trỏ tới file không tồn tại, báo lỗi rõ ràng.

### 8.2. Một đĩa

```text
Final Fantasy VII/
├─ ff7.cue
└─ ff7.bin
```

```text
name: Final Fantasy VII
platform: PS1
launchFile: ff7.cue
```

### 8.3. Nhiều đĩa — dùng `.m3u`

Game như Metal Gear Solid, Final Fantasy VII yêu cầu **đổi đĩa khi đang chơi** và giữ nguyên tiến trình. Chọn đĩa trước khi chạy là không đủ.

```text
Metal Gear Solid/
├─ mgs_disc1.cue
├─ mgs_disc1.bin
├─ mgs_disc2.cue
└─ mgs_disc2.bin
```

Game Center tự sinh `Playlists/PS1/Metal Gear Solid.m3u`:

```text
D:\Games\PS1\Metal Gear Solid\mgs_disc1.cue
D:\Games\PS1\Metal Gear Solid\mgs_disc2.cue
```

- Dùng đường dẫn tuyệt đối vì file `.m3u` nằm ngoài thư mục game.
- Lưu file UTF-8 và kiểm thử với tên có dấu tiếng Việt.
- Game chạy bằng file `.m3u`. Khi game yêu cầu đổi đĩa, người dùng đổi qua menu RetroArch (Disc Control).
- Viết hướng dẫn đổi đĩa bằng hình ảnh, ngắn gọn, hiện khi chạy game nhiều đĩa.

Nhận diện nhiều đĩa: các `.cue` cùng thư mục có mẫu tên `disc1/disc 1/(Disc 1)/CD1`. Nếu không chắc, hỏi người dùng có phải cùng một game không.

### 8.4. `.bin` đơn không có `.cue`

- Không chắc mọi core mở ổn định `.bin` thô → Game Center sinh `.cue` tạm trong `Playlists/PS1/` trỏ tới file `.bin`, rồi chạy file `.cue` đó.
- Kiểm thử thực tế với Beetle PSX HW trước khi chốt cách làm.

### 8.5. Kiểm tra CUE

1. Đọc các dòng `FILE "..." BINARY` trong `.cue`.
2. Kiểm tra file được tham chiếu có tồn tại không (so khớp không phân biệt hoa thường trên Windows).
3. Chấp nhận tên file có khoảng trắng và ký tự Unicode.
4. Cảnh báo nhưng không tự sửa âm thầm.

## 9. Database

### Bảng games

```sql
CREATE TABLE games (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    display_title TEXT,
    platform TEXT NOT NULL,
    launch_file TEXT NOT NULL,
    folder_path TEXT NOT NULL,
    file_size INTEGER,
    file_hash TEXT,
    cover_path TEXT,
    added_at TEXT,
    last_played_at TEXT,
    play_count INTEGER DEFAULT 0,
    is_favorite INTEGER DEFAULT 0,
    scan_status TEXT DEFAULT 'ok'   -- ok | missing | broken_cue | unknown
);
```

### Bảng discs

```sql
CREATE TABLE discs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    disc_number INTEGER NOT NULL,
    file_path TEXT NOT NULL
);
```

Với game nhiều đĩa, `games.launch_file` là file `.m3u` đã sinh.

### Bảng platforms

```sql
CREATE TABLE platforms (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    core_file TEXT NOT NULL,
    extensions_json TEXT NOT NULL,
    enabled INTEGER DEFAULT 1
);
```

### Quét lại

- Không tạo bản ghi trùng.
- Game bị xóa khỏi ổ → `scan_status = 'missing'`, ẩn khỏi danh sách, không xóa ngay lịch sử.
- Game bị di chuyển → khớp theo `file_hash` hoặc (tên + `file_size`) để giữ lịch sử chơi và yêu thích.

## 10. Khởi chạy emulator

### 10.1. Command line

```text
retroarch.exe --config "<DataDir>\Config\retroarch.cfg" -L "<AppDir>\Emulators\RetroArch\cores\<core>.dll" "<launchFile>"
```

`platforms.json`:

```json
{
  "PS1":       { "core": "mednafen_psx_hw_libretro.dll" },
  "SNES":      { "core": "snes9x_libretro.dll" },
  "GBA":       { "core": "mgba_libretro.dll" },
  "MegaDrive": { "core": "genesis_plus_gx_libretro.dll" }
}
```

Tên file `.dll` phải kiểm tra lại theo bản core thực tế đóng gói.

- Dùng `ProcessStartInfo.ArgumentList` (từng tham số riêng) thay vì tự nối chuỗi.
- Đường dẫn có dấu cách và ký tự tiếng Việt phải được kiểm thử.

### 10.2. Cấu hình RetroArch mặc định

Game Center sinh `retroarch.cfg` trong thư mục dữ liệu, gồm:

- Thư mục save, state, system (BIOS) trỏ về thư mục dữ liệu.
- Fullscreen theo lựa chọn của người dùng.
- **Tổ hợp phím thoát game bằng tay cầm** (ví dụ giữ Start + Select). Cần kiểm tra tùy chọn hotkey của phiên bản RetroArch đóng gói.
- Phím Esc trên bàn phím để thoát.
- Tắt các thông báo kỹ thuật không cần thiết trên màn hình.
- Beetle PSX HW: renderer mặc định Vulkan; nếu máy không hỗ trợ Vulkan → software renderer. Không dùng OpenGL renderer làm mặc định vì có lỗi đồ họa.

### 10.3. DuckStation tùy chọn (không đóng gói)

- Trong Cài đặt nâng cao: "Dùng DuckStation mà tôi đã tự cài".
- Người dùng tự tải từ trang chính thức, Game Center chỉ lưu đường dẫn `DuckStation.exe` và truyền file game.
- Không cung cấp cấu hình sẵn cho DuckStation, không tải hộ, không đóng gói.

### 10.4. Sau khi game đóng

- Game Center theo dõi tiến trình RetroArch, khi thoát thì tự hiện lại và đưa cửa sổ lên trước.
- Cập nhật `last_played_at`, `play_count`.

## 11. BIOS

### Chính sách

- Không đóng gói BIOS Sony, Nintendo, Sega hoặc BIOS thương mại.
- Tạo thư mục `BIOS/` rỗng.
- PS1 với Beetle PSX HW: nếu không có BIOS, core dùng **OpenBIOS** → người dùng có thể chơi ngay. BIOS gốc do người dùng tự thêm sẽ cho tương thích tốt hơn.
- Hiển thị hướng dẫn khi platform bắt buộc BIOS mà bị thiếu.

Luồng:

```text
Khởi chạy game
      ↓
Kiểm tra core
      ↓
Platform cần BIOS?
      ↓
Có BIOS → chạy game
Thiếu BIOS nhưng core có thay thế (OpenBIOS/HLE) → chạy game
Không thể chạy → hiển thị hướng dẫn
```

Thông báo:

> Game này cần một file hệ thống BIOS. Hãy thêm BIOS mà bạn tự sao lưu hợp pháp vào thư mục BIOS, sau đó thử lại.

## 12. Giao diện người dùng

### Màn hình chính

```text
┌────────────────────────────────────────┐
│ GAME CENTER                             │
├────────────────────────────────────────┤
│ [Tất cả] [PS1] [SNES] [GBA] [Yêu thích]│
│                                        │
│  [Ảnh] Super Mario World       [Chơi]  │
│  [Ảnh] Final Fantasy VII       [Chơi]  │
│  [Ảnh] Contra                  [Chơi]  │
│                                        │
│ Thoát game: giữ START + SELECT         │
│ [Quét game mới] [Cài đặt] [Trợ giúp]   │
└────────────────────────────────────────┘
```

### Nguyên tắc thiết kế cho người lớn tuổi

- Chữ lớn, tương phản cao.
- Nút tối thiểu 44–50 px chiều cao.
- Không dùng nhiều menu lồng nhau.
- Tên nút rõ ràng: `Chơi`, `Quét game`, `Mở thư mục`.
- Không hiện log kỹ thuật trên màn hình chính.
- **Luôn hiển thị cách thoát game** trên màn hình chính.
- **Điều khiển được bằng tay cầm**: lên/xuống chọn game, A để chơi, đổi tab bằng LB/RB.
- Tự mở fullscreen nếu người dùng chọn.

## 13. Tay cầm

- Dùng cấu hình tay cầm của RetroArch, không tự viết driver.
- Cấu hình mặc định cho tay cầm Xbox/XInput và bàn phím dự phòng.
- Có nút mở phần cấu hình controller.
- Hiển thị hướng dẫn cắm tay cầm.
- Vì chỉ dùng một emulator, cấu hình tay cầm chỉ phải làm một lần cho mọi platform.

## 14. Save game và memory card

- Save và state nằm trong thư mục dữ liệu.
- Nút `Sao lưu dữ liệu` (nén `Saves/` + `States/` thành file zip có ngày).
- Không xóa save khi gỡ Game Center.
- Có nút `Mở thư mục save`.
- Không tự can thiệp nội dung file save.

## 15. Bộ cài đặt

1. Cài theo người dùng (khuyến nghị) hoặc Program Files — chốt một cách.
2. Tạo thư mục dữ liệu `Games`, `BIOS`, `Saves`, `Covers`, `Playlists`.
3. Cài Game Center và RetroArch portable cùng các core đã kiểm tra license.
4. Kèm thư mục `Licenses/` và mã nguồn/đường dẫn mã nguồn theo yêu cầu GPL.
5. Tạo shortcut Desktop (tùy chọn) và Start Menu.
6. Gỡ cài đặt không xóa thư mục dữ liệu.

## 16. Cập nhật phần mềm

Bản đầu chưa cần tự cập nhật. Làm sau:

- Kiểm tra phiên bản Game Center.
- Thông báo có bản mới, tải từ nguồn chính thức.
- Không tự tải BIOS hoặc ROM.
- Không tự thay đổi game người dùng.
- Khi cập nhật RetroArch/core: kiểm tra lại license của phiên bản mới.

## 17. Giấy phép và phân phối

### 17.1. Bảng giấy phép

| Thành phần | Giấy phép | Được đóng gói? | Ghi chú |
|---|---|---|---|
| RetroArch | GPLv3 | Có | Kèm LICENSE và cung cấp mã nguồn hoặc đường dẫn mã nguồn đúng phiên bản |
| Beetle PSX HW | GPL2 | Có | Như trên |
| mGBA | MPL 2.0 | Có | Giữ LICENSE |
| Snes9x | Phi thương mại, riêng | Có, nếu Game Center miễn phí | Phải giữ thông tin giấy phép và bản quyền |
| Genesis Plus GX | Phi thương mại, riêng | Có, nếu Game Center miễn phí | Đọc kỹ file LICENSE của core |
| Core NES, GB/GBC | Chưa chọn | Cần kiểm tra | Chọn core rồi kiểm tra license |
| DuckStation | CC BY-NC-ND 4.0 | **Không** | Chỉ hỗ trợ trỏ tới bản người dùng tự cài |

Nguồn tham khảo: trang Licenses của Libretro Docs (liệt kê các core phi thương mại, có đường dẫn tới văn bản giấy phép gốc).

### 17.2. Nguyên tắc bắt buộc

- Game Center **luôn miễn phí**, không bán, không quảng cáo, không có tính năng trả phí.
- Nếu sau này muốn thương mại hóa: phải bỏ Snes9x, Genesis Plus GX và mọi core phi thương mại, thay bằng core GPL/MPL (ví dụ bsnes cho SNES — cần kiểm tra license trước).
- Giữ file `LICENSE`, `NOTICE` của từng thành phần, đúng phiên bản đóng gói.
- Ghi rõ thành phần nguồn mở trong `Trợ giúp → Giấy phép`.
- Không đưa ROM/ISO/BIOS thương mại vào bộ cài.
- Không dùng logo thương hiệu khiến người dùng hiểu đây là sản phẩm chính thức của Sony, Nintendo hoặc Sega.

### 17.3. Donate

Các giấy phép phi thương mại không nói rõ về việc nhận donate, nên đây là vùng chưa chắc chắn. Nếu nhận donate:

- Phần mềm tải về hoàn toàn miễn phí, không khóa tính năng, không có bản riêng cho người donate.
- Nút donate đặt ở trang web hoặc mục "Giới thiệu", không đặt trong bộ cài hay màn hình chơi game.
- Ghi rõ donate là ủng hộ phần launcher Game Center, không phải trả tiền cho emulator.
- Trước khi mở donate: hỏi trực tiếp tác giả Snes9x và Genesis Plus GX, lưu lại văn bản trả lời.

### 17.4. Thư mục license

```text
Licenses/
├─ GameCenter-LICENSE.txt
├─ RetroArch-LICENSE.txt
├─ RetroArch-SOURCE.txt        (đường dẫn mã nguồn đúng phiên bản)
└─ Core-LICENSES/
   ├─ beetle-psx-hw.txt
   ├─ mgba.txt
   ├─ snes9x.txt
   └─ genesis-plus-gx.txt
```

## 18. Kiểm thử

### Scanner

- File có dấu tiếng Việt, dấu cách, nhiều dấu chấm.
- `.cue` trỏ sai `.bin`; tên trong `.cue` khác hoa/thường.
- Game `.bin` đơn PS1 (sinh `.cue` tạm).
- `.bin` Mega Drive ngoài thư mục platform (nhận diện header `SEGA`).
- Game nhiều đĩa (sinh `.m3u`).
- `.zip` chứa một ROM, nhiều ROM, ROM không nhận ra.
- File trùng tên.
- Quét lại không tạo bản ghi trùng; game bị xóa; game bị di chuyển.

### Emulator

- Khởi chạy từ đường dẫn có dấu cách và tiếng Việt.
- Fullscreen.
- Thoát bằng tổ hợp tay cầm và bằng Esc, launcher hiện lại.
- Đổi đĩa giữa game qua `.m3u`.
- PS1 không có BIOS (OpenBIOS) và có BIOS.
- Beetle PSX HW trên máy có và không có Vulkan.
- Tay cầm Xbox/XInput, bàn phím dự phòng.
- Save/load.
- PS1 game Việt hóa.

### Launcher

- Điều khiển toàn bộ bằng tay cầm.
- Màn hình độ phân giải thấp và scale 125–150%.

### Bộ cài

- Máy Windows mới.
- Máy không có quyền administrator.
- Đường dẫn cài có dấu cách.
- Gỡ ứng dụng không xóa game/save.
- Nâng cấp giữ nguyên dữ liệu.

## 19. Lộ trình phát triển

### Milestone 1 — Launcher tối thiểu

- Tạo project C# WPF.
- Tách thư mục ứng dụng và dữ liệu.
- Đọc `settings.json`, sinh `retroarch.cfg`.
- Quét game, hiển thị danh sách.
- Chạy SNES và PS1 (Beetle PSX HW) qua RetroArch.
- Tổ hợp phím thoát game, launcher tự hiện lại.

### Milestone 2 — PS1 hoàn chỉnh

- Ghép `.cue/.bin`.
- `.bin` đơn (sinh `.cue` tạm).
- Kiểm tra CUE lỗi.
- Nhiều đĩa với `.m3u` và hướng dẫn đổi đĩa.
- Nút mở thư mục game.

### Milestone 3 — Dễ sử dụng

- Nút lớn, tìm kiếm, lọc platform.
- Điều khiển launcher bằng tay cầm.
- Làm sạch tên hiển thị.
- Yêu thích, lịch sử chơi.
- Phân loại `.zip`, header Mega Drive.
- Cấu hình tay cầm.

### Milestone 4 — Hoàn thiện

- Nhận diện hash, ảnh bìa.
- Sao lưu save.
- Hướng dẫn BIOS.
- Màn hình license.
- Bộ cài Next → Next → Finish.
- Kiểm thử trên máy sạch.

## 20. Checklist bản phát hành đầu tiên

- [ ] Game Center chạy được trên Windows sạch, không cần quyền admin (nếu chọn cài per-user).
- [ ] Quét SNES, GBA, Mega Drive và PS1.
- [ ] PS1 `.cue/.bin` hiển thị một lần.
- [ ] PS1 `.bin` đơn chạy được.
- [ ] Game nhiều đĩa đổi đĩa được khi đang chơi (`.m3u`).
- [ ] PS1 chạy được khi không có BIOS (OpenBIOS).
- [ ] Thoát game bằng tay cầm, launcher hiện lại.
- [ ] Không có DuckStation trong bộ cài.
- [ ] Không có ROM và BIOS thương mại trong bộ cài.
- [ ] Có thư mục BIOS rỗng và hướng dẫn.
- [ ] Có đầy đủ LICENSE từng core đúng phiên bản và đường dẫn mã nguồn GPL.
- [ ] Không có quảng cáo hay tính năng trả phí.
- [ ] Có nút mở thư mục game.
- [ ] Gỡ phần mềm không xóa Games/Saves.
- [ ] Kiểm tra tay cầm phổ biến.
- [ ] Có log để hỗ trợ khi lỗi.

## 21. Kết luận

Game Center là frontend nhẹ, miễn phí, chạy mọi platform qua RetroArch. Bản đầu tập trung vào SNES, GBA, Mega Drive và PS1. PS1 dùng Beetle PSX HW (GPL2), giữ nguyên `.bin/.cue`, hỗ trợ game Việt hóa, đổi đĩa bằng `.m3u`, chạy được không cần BIOS nhờ OpenBIOS. DuckStation không được đóng gói do giấy phép.

Mục tiêu của bản đầu:

```text
Người dùng không cần biết emulator là gì.
Người dùng chỉ cần chép game, bấm Chơi, và biết cách thoát.
```
