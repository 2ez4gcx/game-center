# Nhận diện game trong file nén và định dạng lạ

Tài liệu nghiên cứu: người dùng chép vào `Games/` đủ loại file (nén, chia phần, đổi đuôi, ảnh đĩa đủ kiểu). Game Center cần **tự biết đó là game của máy nào**, chạy được thì chạy, không chạy được thì **nói rõ lý do và cách xử lý** thay vì bỏ qua im lặng.

Ký hiệu trạng thái:

| | Ý nghĩa |
|---|---|
| ✅ | Đã có từ trước (0.2.0) |
| 🆕 | Làm trong bản 0.3.0 |
| 💡 | Đề xuất, chưa làm |
| ⛔ | Không làm (lý do ghi kèm) |

## 1. Ba lớp nhận diện

Mỗi file đi qua 3 lớp, lớp sau chỉ dùng khi lớp trước không chắc chắn:

1. **Vỏ (container):** file là zip, 7z, rar, chd, cso, pbp, nhiều phần...? Đọc vài byte đầu (magic number), **không tin đuôi file** (`.zip` có thể là 7z đổi đuôi).
2. **Nội dung:** header ROM băng, hệ thống file ISO 9660 của ảnh đĩa, metadata của ảnh nén. Đây là lớp chính xác nhất.
3. **Ngữ cảnh:** thư mục người dùng đặt (`Games/PS2/...`), tên file (`(Disc 2)`, `(Track 3)`, mã đĩa `SLUS-20062`), và về sau là CRC/hash so với cơ sở dữ liệu.

Nguyên tắc giữ nguyên từ bản đầu: **không ghi vào thư mục game**, không đổi tên file, file sinh ra (`.cue`, `.m3u` tạm) nằm trong `Playlists/`.

## 2. Ràng buộc từ RetroArch và core

Quyết định "chạy được hay phải giải nén" phụ thuộc vào cách RetroArch đọc file:

| Core | Cần đường dẫn file thật (`needs_fullpath`) | Chạy thẳng từ `.zip`? |
|---|---|---|
| FCEUmm, Snes9x, Gambatte, mGBA | Không | ✅ Có, cú pháp `game.zip#rom.nes` |
| Genesis Plus GX | Có | ⚠️ RetroArch giải nén **file đầu tiên khớp đuôi** ra thư mục tạm rồi chạy |
| Beetle PSX HW (PS1) | Có | ⚠️ Như trên: `.cue + .bin` trong zip **hỏng** vì chỉ 1 file được giải nén |
| LRPS2 (PS2) | Có | ⚠️ Như trên, và ảnh đĩa PS2 4 GB giải nén mỗi lần chơi là quá chậm |

RetroArch đọc được **zip** (deflate / store) và **7z**; **không** đọc rar. Zip nén bằng LZMA / BZip2 / Zstandard (7-Zip, WinRAR cho phép chọn) thì RetroArch không mở được.

Kết luận: **ROM băng trong zip → chạy thẳng; game đĩa trong file nén → yêu cầu giải nén.**

## 3. Bảng định dạng vỏ / nén

| Định dạng | Nhận biết | Hiện trạng | Xử lý |
|---|---|---|---|
| `.zip` | `PK\x03\x04` | ✅ đọc danh sách file, CRC32 có sẵn trong zip (không cần giải nén) | ROM băng: chạy `zip#rom`. Zip nhiều ROM: mỗi ROM một game |
| `.zip` thật ra là 7z / rar | `7z\xBC\xAF\x27\x1C` / `Rar!\x1A\x07` | 🆕 | Báo "hãy giải nén" thay vì "zip bị hỏng" |
| `.zip` có mật khẩu | cờ mã hóa của mục trong zip | 🆕 | Báo "zip có mật khẩu, hãy giải nén" |
| `.zip` nén LZMA / BZip2 / Zstd | mở được zip nhưng không đọc được mục | 🆕 | Báo "hãy giải nén" |
| `.zip` chia nhiều phần (`.z01`, `.z02`, `.zip`) | có file `.z01` cùng tên | 🆕 | Báo "file nén nhiều phần"; ẩn các phần `.zNN` |
| `.7z` | đuôi + magic | 🆕 báo giải nén | 💡 đọc bằng SharpCompress (MIT) để nhận diện; ROM băng có thể chạy thẳng `game.7z#rom` |
| `.rar`, `.rar` cũ nhiều phần (`.r00`...) | đuôi + magic | 🆕 báo giải nén, ẩn `.r00`... | 💡 Game Center tự giải nén vào `Cache/` (RetroArch không đọc rar) |
| Nhiều phần `.7z.001`, `.zip.001`, `.part1.rar` | tên file | 🆕 chỉ báo phần đầu, ẩn phần sau | Hướng dẫn giải nén từ phần đầu |
| `.gz`, `.tar`, `.xz`, `.bz2`, `.zst` | đuôi | 🆕 báo giải nén | LRPS2 đọc được `.gz` ảnh đĩa PS2 (gzip có index của PCSX2): 💡 nhận theo thư mục `PS2/` |
| `.chd` (MAME) | `MComprHD` | 🆕 đọc metadata: `CHT2/CHTR/CHCD` = CD, `DVD ` = DVD, `CHGD/CHGT` = GD-ROM | DVD → PS2; CD → PS1 (PS2 đĩa CD theo thư mục `PS2/`); GD-ROM → máy khác. 💡 Giải nén hunk đầu (codec `cdzl` = zlib, `cdlz` = LZMA) để đọc `SYSTEM.CNF` của CD |
| `.cso` (CISO) | `CISO` | 🆕 giải nén khối chứa sector 16 (deflate) và `SYSTEM.CNF` | PS2 hoặc PSP (chưa hỗ trợ) |
| `.zso` | đuôi | 🆕 nhận là PS2 (LZ4, chỉ PCSX2 dùng) | 💡 đọc khối LZ4 để chắc chắn |
| `.pbp` (EBOOT) | `\0PBP`, đầu phần PSAR | 🆕 `PSISOIMG` / `PSTITLEIMG` = PS1; còn lại = PSP | PS1 chạy bằng Beetle; PSP báo chưa hỗ trợ |
| `.ecm` | đuôi (`ECM\0`) | 🆕 báo dùng `unecm` | ⛔ không tự giải: hiếm, cần công cụ riêng |
| `.rvz`, `.wia`, `.gcz`, `.wbfs` | đuôi | 🆕 báo "GameCube / Wii chưa hỗ trợ" | — |
| `.nsp`, `.xci`, `.nsz`, `.xcz` | đuôi | 🆕 báo "Switch chưa hỗ trợ" | ⛔ |

## 4. Chữ ký nội dung từng hệ máy

Đọc tối đa 32 KB đầu file (`RomHeader`) hoặc vài sector ảnh đĩa (`DiscImage`):

| Hệ máy | Chữ ký | Trạng thái |
|---|---|---|
| NES | `NES\x1A` ở 0 | 🆕 (trước chỉ theo đuôi `.nes`) |
| Famicom Disk System | `FDS\x1A` ở 0 hoặc `*NINTENDO-HVC*` ở 1 | 🆕 báo chưa hỗ trợ · 💡 FCEUmm chạy được nếu có BIOS `disksys.rom` |
| Game Boy / Color | logo Nintendo ở `0x104` + tổng kiểm tra header ở `0x14D`; cờ CGB `0x143` (`0x80`/`0xC0` = Color) | 🆕 |
| Game Boy Advance | `0x96` ở `0xB2` + byte bù ở `0xBD` | 🆕 |
| Nintendo DS | CRC logo `0xCF56` ở `0x15C` | 🆕 báo chưa hỗ trợ |
| Mega Drive | `SEGA` ở `0x100` (hoặc `0x101`) | ✅ |
| Sega 32X | `SEGA 32X` ở `0x100` | 🆕 báo chưa hỗ trợ (Genesis Plus GX không chạy 32X) |
| Master System / Game Gear | `TMR SEGA` ở `0x7FF0` (`0x3FF0`, `0x1FF0`); nửa cao byte cuối: 3–4 = SMS, 5–7 = GG | 🆕 **hỗ trợ mới**, dùng luôn Genesis Plus GX đã đóng gói |
| Nintendo 64 | `80 37 12 40` / `37 80 40 12` / `40 12 37 80` | 🆕 báo chưa hỗ trợ |
| SNES | header nội ở `0x7FC0` (LoROM) / `0xFFC0` (HiROM), +`0x200` nếu có header copier; checksum + bù = `0xFFFF` | 💡 (đuôi `.sfc/.smc` vẫn đủ trong đa số trường hợp) |
| PS1 / PS2 (CD, DVD) | ISO 9660: `CD001` ở sector 16, system id `PLAYSTATION`; **`SYSTEM.CNF` có `BOOT2` = PS2, `BOOT` = PS1**; thiếu `SYSTEM.CNF` = PS1 đời đầu | 🆕 |
| PSP | system id `PSP GAME` | 🆕 báo chưa hỗ trợ |
| Sega CD / Saturn / máy khác | ISO 9660 nhưng system id khác (`SEGADISCSYSTEM`, `SEGA SEGASATURN`...) | ✅ báo "đĩa của máy khác" |
| GameCube / Wii | magic `C2 33 9F 3D` ở `0x1C` / `5D 1C 9E A3` ở `0x18` | 💡 báo chưa hỗ trợ khi file đuôi `.iso` |
| Xbox | `MICROSOFT*XBOX*MEDIA` ở `0x10000` | 💡 như trên |

Ảnh đĩa được đọc theo 3 kiểu sector: 2048 byte (`.iso` DVD), 2352 byte thô (`.bin` CD, byte 15 là mode: Mode 1 dữ liệu từ byte 16, Mode 2 từ byte 24), và khối nén `.cso`.

## 5. Danh mục trường hợp

### 5.1. File `.zip`

| # | Trường hợp | Nhận biết | Xử lý | |
|---|---|---|---|---|
| Z1 | 1 ROM băng | đuôi mục trong zip | chạy `game.zip`, hash = CRC32 trong zip | ✅ |
| Z2 | Nhiều ROM (cùng hoặc khác máy) | ≥ 2 mục nhận ra | mỗi ROM một game `zip#rom` | ✅ |
| Z3 | ROM kèm ảnh, `.txt`, `.nfo` | bỏ qua mục không phải ROM | như Z1 | ✅ |
| Z4 | ROM đuôi `.bin` / `.rom` / không đuôi | đọc header mục | NES, GB/GBC, GBA, Mega Drive, SMS, GG | 🆕 |
| Z5 | Thư mục bên trong zip theo hệ máy (`SNES/x.smc`) | tên thư mục trong đường dẫn mục | dùng làm gợi ý hệ máy | 🆕 |
| Z6 | Rác macOS `__MACOSX/`, `._tên` | tên mục | bỏ qua (trước đây `._game.sfc` bị coi là ROM) | 🆕 |
| Z7 | Game đĩa (`.cue+.bin`, `.iso`, `.chd`, `.pbp`) | đuôi mục, hoặc `.bin` có sync CD | báo "game đĩa phải giải nén" | 🆕 |
| Z8 | Zip lồng zip / chứa 7z, rar | đuôi mục | báo "hãy giải nén" | 🆕 |
| Z9 | Bộ ROM arcade (MAME / FBNeo / Neo Geo): `kof98.zip` chứa `242-p1.p1`... | ≥ 3 mục, đa số đuôi lạ, không có `.exe` | báo "game arcade, chưa hỗ trợ" | 🆕 · 💡 hỗ trợ arcade cần core FBNeo (giấy phép phi thương mại) và đúng phiên bản romset |
| Z10 | Có mật khẩu | cờ mã hóa | báo | 🆕 |
| Z11 | Nén LZMA / BZip2 / Zstd | không đọc được mục | báo "hãy giải nén" | 🆕 |
| Z12 | Đuôi `.zip` nhưng là 7z / rar | magic | báo "hãy giải nén" | 🆕 |
| Z13 | Zip hỏng / tải dở | không mở được | báo "zip bị hỏng" | ✅ |
| Z14 | Zip chia phần `.z01` | có file `.z01` | báo "nhiều phần" | 🆕 |
| Z15 | Tên mục tiếng Việt không có cờ UTF-8 (zip tạo bằng Windows cũ) | cờ bit 11 | 💡 thử giải mã CP437 / CP1258; cần kiểm tra RetroArch nhận đúng tên `zip#mục` | 💡 |
| Z16 | Zip > 4 GB (Zip64, ảnh đĩa PS2) | .NET đọc được Zip64 | thuộc Z7: báo giải nén | 🆕 |
| Z17 | Zip có ROM + bản vá Việt hóa (`.ips`, `.bps`, `.ups`) | đuôi mục | 💡 hiện bản Việt hóa như một game riêng; RetroArch tự vá khi file vá đặt cạnh ROM cùng tên | 💡 |
| Z18 | Zip chứa BIOS (`neogeo.zip`, `scph1001.bin`) | tên trong danh sách BIOS | 💡 tự chép vào thư mục BIOS | 💡 |

### 5.2. Ảnh đĩa

| # | Trường hợp | Xử lý | |
|---|---|---|---|
| D1 | `.cue + .bin` PS1 | ✅ hiện một lần, kiểm tra file thiếu | ✅ |
| D2 | `.cue + .bin` **PS2** (game đĩa CD) | đọc track 1 → `BOOT2` → PS2 | 🆕 |
| D3 | `.bin` không có `.cue` | PS1 → sinh `.cue` tạm; PS2 → chạy thẳng `.bin` (LRPS2 tự nhận cỡ sector) | ✅ / 🆕 |
| D4 | Nhiều track `Tên (Track 1).bin`, `(Track 2).bin`... mất `.cue` | sinh `.cue` tạm: track 1 dữ liệu, các track sau AUDIO (`INDEX 00 00:00:00`, `INDEX 01 00:02:00` theo Redump) — trước đây mỗi track hiện thành một game lỗi | 🆕 |
| D5 | Nhiều đĩa `.cue` `(Disc 1)`, `(Disc 2)` | ✅ sinh `.m3u`; nay ghép theo **từng hệ máy** | ✅ / 🆕 |
| D6 | Nhiều đĩa nằm rời dạng `.chd` / `.pbp` / `.iso` / `.bin` lẻ | ghép thành một game + `.m3u` để đổi đĩa (trước đây thành nhiều game riêng, không đổi đĩa được) | 🆕 |
| D7 | `.iso` PS2 (DVD) | đọc `SYSTEM.CNF` | 🆕 |
| D8 | `.iso` PS1 (2048 byte, mất dữ liệu Mode 2) | báo "Beetle không mở được, dùng .bin/.cue hoặc .chd" | 🆕 |
| D9 | `.iso` PSP / máy khác | báo chưa hỗ trợ | 🆕 |
| D10 | `.chd` DVD | PS2 | 🆕 |
| D11 | `.chd` CD | PS1, hoặc PS2 nếu nằm trong `Games/PS2` | 🆕 · 💡 giải nén hunk để chắc chắn |
| D12 | `.ccd + .img + .sub` (CloneCD) | PS1: chạy `.ccd`; PS2: chạy `.img` | 🆕 |
| D13 | `.mdf + .mds` (Alcohol), `.nrg` (Nero) | PS2: chạy thẳng (LRPS2 hỗ trợ); PS1: báo đổi định dạng | 🆕 |
| D14 | `.img` lẻ | như `.bin` lẻ | 🆕 |
| D15 | `.gdi`, `.cdi` (Dreamcast) | báo chưa hỗ trợ | 🆕 |
| D16 | `.cue` trỏ sai tên / khác hoa thường | ✅ khớp không phân biệt hoa thường, báo file thiếu | ✅ |
| D17 | `.bin` audio track với `.cue` dùng `.ogg/.flac/.wav` | Beetle hỗ trợ một phần | 💡 kiểm thử |
| D18 | Ảnh đĩa sector 2448 (kèm subchannel, `.mdf`/`.img` một số công cụ) | 💡 thêm kiểu sector 2448 vào `DiscImage` | 💡 |

### 5.3. File lẻ, tên file và thư mục

| # | Trường hợp | Xử lý | |
|---|---|---|---|
| F1 | ROM đúng đuôi | ✅ theo đuôi + thư mục | ✅ |
| F2 | ROM sai đuôi / đuôi chung `.bin`, `.rom` | đọc header (mục 4) | 🆕 |
| F3 | Game của máy chưa hỗ trợ (`.n64`, `.nds`, `.pce`, `.ws`...) | hiện ở "Chưa nhận ra" với tên máy, thay vì bỏ qua | 🆕 |
| F4 | BIOS chép nhầm vào `Games/` (BIOS PS2 4 MB, `scph1001.bin`) | không hiện như game; tự chép sang `BIOS/` (BIOS PS2 vào `BIOS/pcsx2/bios`) | 🆕 |
| F5 | BIOS PS2 chép thẳng vào `BIOS/` hoặc `BIOS/PS2/` | tự chép sang `BIOS/pcsx2/bios/` (nơi LRPS2 tìm), kèm `.rom1/.rom2/.erom/.nvm` cùng tên | 🆕 |
| F6 | File rác macOS / Windows / USB (`._tên`, `.DS_Store`, `Thumbs.db`, `$RECYCLE.BIN`, `System Volume Information`) | bỏ qua | 🆕 |
| F7 | Save / thẻ nhớ chép nhầm vào `Games/` (`.srm`, `.mcr`, `.sav`, `.ps2`) | 💡 gợi ý chép vào `Saves/` | 💡 |
| F8 | Mã đĩa trong tên file (`SLUS-20062` → PS2, `SLUS-00594` → PS1) | 💡 gợi ý bổ sung cho `.chd` CD; số PS2 thường ≥ 20000 (SLUS/SCUS-97xxx), PS1 < 10000 / SCUS-94xxx — có ngoại lệ nên chỉ làm gợi ý | 💡 |
| F9 | Đang chép dở (file lớn) | ✅ chờ 3 giây sau thay đổi cuối mới quét | ✅ |
| F10 | Trùng game: bản zip và bản giải nén cùng thư mục | 💡 so CRC32 (zip có sẵn) để ẩn bản trùng | 💡 |

## 6. Lộ trình đề xuất

| Giai đoạn | Nội dung | Chi phí |
|---|---|---|
| **A — 0.3.0 (xong)** | Toàn bộ mục 🆕 ở trên | — |
| B | Thư viện **SharpCompress** (MIT): đọc danh sách và header ROM trong `.7z`; ROM băng chạy thẳng `game.7z#rom` | Thấp, thêm ~1 MB |
| C | Nút **"Giải nén giúp tôi"** cho game đĩa trong zip/7z/rar: giải nén vào thư mục `Extracted/` trong thư mục dữ liệu (không ghi vào thư mục game của người dùng; hỏi trước, báo dung lượng cần) | Trung bình; cần kiểm soát dung lượng ổ |
| D | Nhận diện theo **CRC32 / SHA1** với cơ sở dữ liệu No-Intro / Redump (tên chuẩn, ảnh bìa, đúng hệ máy cả khi đổi tên). Zip có sẵn CRC32 nên rất nhanh | Trung bình; **phải kiểm tra giấy phép** của file DAT trước khi đóng gói |
| E | Giải nén hunk `.chd` (zlib, LZMA) để đọc `SYSTEM.CNF` của đĩa CD | Trung bình; map của CHD v5 nén Huffman |
| F | Hệ máy mới dùng core sẵn có: Famicom Disk System (FCEUmm + BIOS), Sega CD (Genesis Plus GX + BIOS), SG-1000 | Thấp |

## 7. Rủi ro

- **Nhận nhầm:** header ROM là kiểm tra phỏng đoán. Luôn ưu tiên đuôi file rõ ràng và thư mục người dùng đặt; header chỉ dùng cho đuôi chung (`.bin`, `.rom`) hoặc không có đuôi. Người dùng luôn sửa được bằng **Chọn hệ máy** (được giữ khi quét lại).
- **Hiệu năng:** chỉ đọc phần đầu file (≤ 32 KB, BIOS PS2 ≤ 512 KB, vài sector ảnh đĩa), không đọc cả ảnh đĩa nhiều GB. Quét lại mỗi khi thư mục game thay đổi.
- **Đổi hành vi với dữ liệu cũ:** game đĩa rời nhiều đĩa nay gộp thành một game. Bản ghi cũ của đĩa 1 được giữ (cùng khóa file gốc) nên lịch sử chơi và yêu thích không mất; đĩa 2+ chuyển sang "không còn tìm thấy".
- **Giấy phép:** không đóng gói BIOS, ROM, cơ sở dữ liệu chưa rõ giấy phép.
