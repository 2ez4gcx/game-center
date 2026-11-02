# Bảo toàn save và game nhiều đĩa

Mục tiêu: **người chơi không bao giờ mất save**, game nhiều đĩa (The Legend of Dragoon, Final Fantasy VII/VIII/IX, Metal Gear Solid...) **chơi hết được từ đĩa 1 tới đĩa cuối**, và mọi thay đổi được **kiểm thử với RetroArch thật** trước khi phát hành.

Ký hiệu: ✅ đã có từ trước · 🆕 làm trong 0.3.0 · 🧪 có kiểm thử tự động · 💡 đề xuất.

## 1. Save nằm ở đâu

| Loại | Vị trí | Đặt tên theo |
|---|---|---|
| Save trong game: SRAM băng, thẻ nhớ PS1 | `Saves/<core>/<tên>.srm` (ví dụ `Saves/Beetle PSX HW/LOD.srm`) | **Tên file chạy** bỏ đuôi: file `.cue`, `.chd`, `.m3u`... |
| Thẻ nhớ PS2 | `BIOS/pcsx2/memcards/Mcd001.ps2`, `Mcd002.ps2` | **Dùng chung mọi game** như máy thật (`pcsx2_shared_memory_cards = enabled`) |
| Đĩa đang dùng của game nhiều đĩa | `Saves/<core>/<tên>.ldci` | Tên file chạy |
| Chỗ đang chơi (save state) | `States/<core>/<tên>.state.auto` | Tên file chạy |
| Sao lưu tự động | `Backups/Auto/<tên>/<thời gian>/` | Tên file chạy |
| Game trùng tên file chạy với game thêm trước | `Saves/_PerGame/<id>/<core>/<tên>.srm`, `States/_PerGame/<id>/...`, sao lưu `Backups/Auto/<tên> [<id>]/` | Tên file chạy, thư mục riêng theo mã game |

Điểm mấu chốt: **RetroArch tìm save theo tên file chạy**. Đổi tên file chạy thì save cũ vẫn còn trên ổ nhưng "biến mất" với người chơi. Hai game trùng tên file chạy thì **dùng chung và ghi đè** save của nhau.

## 2. Các tình huống mất save

### A. Tên file chạy đổi → save cũ không được tìm thấy

| # | Tình huống | Xử lý | |
|---|---|---|---|
| A1 | Chơi đĩa 1 riêng lẻ (`LOD (Disc 1).srm`), sau đó chép thêm đĩa 2–4 → game thành bộ nhiều đĩa chạy bằng `LOD.m3u` | Trước khi chơi, **nhận lại** save: tên hiện tại chưa có `.srm` thì **chép** (không di chuyển) bản mới nhất từ các tên game từng dùng: lịch sử tên (bảng `save_names`), file gốc, từng đĩa | 🆕 🧪 |
| A2 | Mỗi đĩa một thư mục, file cùng tên `LOD.cue` | `.m3u` đặt theo tên file đĩa 1 (`LOD.m3u`) → thẻ nhớ `LOD.srm` của đĩa 1 dùng tiếp, không cần chép | 🆕 🧪 |
| A3 | Người dùng đổi tên file / thư mục, bỏ "(USA)" | Như A1 nếu Game Center nhận ra là cùng game (khớp theo hash hoặc tên + dung lượng) | 🆕 🧪 |
| A4 | Giải nén `.zip` thành file ROM, đổi `.cue` sang `.chd` | Như A3 | 🆕 |
| A5 | Bản Game Center mới đổi quy tắc đặt tên (chính bản 0.3.0: ghép đĩa rời, ghép đĩa theo thư mục) | Như A1; game đã chơi ở bản cũ chưa có lịch sử tên vẫn tìm qua tên file gốc và tên từng đĩa | 🆕 🧪 |
| A6 | "Chỗ đang chơi" (`.state.auto`) cũng theo tên | Nhận lại state cũ như A1 | 🆕 🧪 |
| A7 | Đổi thư mục dữ liệu trong Cài đặt → thư mục mới trống | Tự chép Saves, States, BIOS, Backups sang (không ghi đè, không xóa thư mục cũ) | 🆕 🧪 |

Chỉ nhận lại save cho game **đã từng chơi** (số lần chơi > 0 hoặc có lịch sử tên). Game mới thêm chưa có save nào, nên không bao giờ nhận nhầm save của game khác.

### B. Hai game dùng chung một save → ghi đè nhau

| # | Tình huống | Xử lý | |
|---|---|---|---|
| B1 | **Mọi game PS1 dạng `EBOOT.PBP`** (tên file luôn là `EBOOT`) dùng chung `EBOOT.srm` — lỗi có sẵn ở 0.2.0 | Game thêm sau (cùng core, trùng tên save với game có mã nhỏ hơn) lưu save và chỗ đang chơi ở **thư mục riêng** `Saves/_PerGame/<id>/`, `States/_PerGame/<id>/`, truyền cho RetroArch bằng `--appendconfig`. **File game giữ nguyên** nên không ảnh hưởng việc nạp game (cách bọc `.m3u` đã bỏ vì chưa kiểm chứng được `.pbp` trong `.m3u`). Game thêm trước giữ chỗ cũ. Game thêm sau **đã chơi ở bản cũ** nhận **bản sao** thẻ chung một lần; game mới không bao giờ nhận thẻ của game khác | 🆕 🧪 E2E |
| B2 | `game.cue`, `disc.cue`, `LOD.cue` trùng ở nhiều thư mục (bản Việt hóa và bản gốc) | Như B1 | 🆕 🧪 E2E |
| B3 | Hai ROM băng trùng tên ở hai thư mục (`rom.nes`, `game.gb`) | Như B1 (thư mục save riêng dùng được cho mọi core) | 🆕 🧪 E2E |
| B5 | Thư mục save riêng chưa tồn tại: RetroArch **bỏ qua** cấu hình và lưu vào thư mục mặc định cạnh `retroarch.exe` — tức **thư mục cài đặt**, mất khi cài lại (phát hiện nhờ kiểm thử đầu-cuối) | Tạo thư mục trước khi chơi; chưa tạo được thì không truyền cấu hình (dùng chỗ chung) | 🆕 🧪 E2E |
| B4 | Thẻ nhớ PS2 dùng chung mọi game | Đúng như máy PS2 thật (một thẻ nhớ nhiều game); không phụ thuộc tên file nên đổi tên / ghép đĩa không ảnh hưởng | 🆕 |

### C. Save bị ghi đè bằng dữ liệu cũ hoặc hỏng

| # | Tình huống | Xử lý | |
|---|---|---|---|
| C1 | **"Chơi tiếp" kéo save trong game về quá khứ** (lỗi có sẵn ở 0.2.0): save state của máy băng chứa cả SRAM. Người chơi save trong game sau lần lưu chỗ đang chơi, rồi bấm "Chơi tiếp" → SRAM bị thay bằng bản trong state, RetroArch ghi đè `.srm` → **mất save mới nhất** | `block_sram_overwrite = true`: nạp state không đụng tới SRAM, save trong game mới nhất được giữ (vẫn load được trong game) | 🆕 🧪 (đo được: 0.2.0 mất save, 0.3.0 giữ save) |
| C2 | Mất điện / tắt ngang khi đang chơi | Save trong game ghi xuống ổ mỗi 10 giây (`autosave_interval`) | ✅ |
| C3 | Save bị hỏng, lỡ chép đè thẻ nhớ khác vào, chơi hỏng tiến trình | **Sao lưu tự động trước mỗi lần chơi** (chỉ khi save đổi so với bản trước, giữ 10 bản/game, thẻ nhớ PS2 kèm theo với game PS2) + **"⋯ → Khôi phục save cũ"** chọn theo thời gian. Khôi phục cũng sao lưu bản hiện tại trước nên đổi ý được | 🆕 🧪 |
| C4 | Chọn "Không lưu" chỗ đang chơi | Chỉ bỏ save state mới, khôi phục state cũ; không đụng save trong game | ✅ 🧪 |
| C5 | Lỡ đổi cách lưu thẻ nhớ trong menu RetroArch (Beetle "Mednafen" → save thành `.mcr` chỗ khác; LRPS2 thẻ nhớ riêng từng game) | Game Center ghi lại tùy chọn core mỗi lần chạy: `beetle_psx_hw_use_mednafen_memcard0_method = libretro`, `pcsx2_shared_memory_cards = enabled` | 🆕 🧪 |
| C6 | Gỡ cài đặt / nâng cấp | Bộ cài không bao giờ xóa `Saves`, `States`, `BIOS`, `Backups` | ✅ · checklist |
| C7 | Dùng DuckStation tự cài cho PS1 | Save nằm trong thư mục của DuckStation, không dùng chung với Beetle | ✅ ghi rõ trong Cài đặt |
| C8 | Ổ đầy / file bị khóa (đồng bộ đám mây, phần mềm diệt virus) | 💡 kiểm tra dung lượng trống trước khi chơi, báo nếu RetroArch không ghi được save (đọc log) | 💡 |
| C9 | Sao lưu thủ công | "Cài đặt → Sao lưu dữ liệu" nén Saves, States và thẻ nhớ PS2 | ✅ / 🆕 thêm thẻ nhớ PS2 |

## 3. Game nhiều đĩa

| # | Tình huống | Xử lý | |
|---|---|---|---|
| M1 | Đĩa `.cue/.bin` cùng thư mục, tên có `(Disc N)` | Ghép một game + `.m3u` | ✅ 🧪 |
| M2 | Đĩa `.chd` / `.pbp` / `.iso` / `.bin` lẻ để rời | Ghép | 🆕 🧪 |
| M3 | Mỗi đĩa một thư mục: `The Legend of Dragoon (Disc 1) (VN)/LOD.cue`... hoặc `Tên/CD1/`, `Tên/CD2/` | Ghép các thư mục anh em cùng tên | 🆕 🧪 |
| M4 | Thiếu đĩa ở giữa (có 1, 3, 4) | Vẫn chơi được, hiện cảnh báo "⚠ Thiếu đĩa 2" | 🆕 🧪 |
| M5 | Trộn định dạng (đĩa 1 `.cue`, đĩa 2 `.chd`) cùng thư mục | Ghép | 🆕 |
| M6 | Đĩa 1 bản Việt hóa, đĩa 2–4 bản gốc (tên khác nhau) | Không tự ghép được | 💡 nút "Ghép đĩa" chọn tay; hiện tại tự tạo `.m3u` (Game Center đọc được `.m3u` người dùng tạo) |
| M7 | Đổi đĩa khi đang chơi | Menu RetroArch (L3 + R3 hoặc F1) → Điều khiển đĩa; Game Center hiện hướng dẫn trước khi chơi game nhiều đĩa | ✅ 🧪 (core báo hỗ trợ đổi đĩa, nạp 2 đĩa) |
| M8 | Thoát khi đang ở đĩa 3, lần sau mở lại | RetroArch ghi đĩa đang dùng vào `<tên>.ldci` và mở lại đúng đĩa đó; Game Center giữ đường dẫn trong `.m3u` ổn định nên file này luôn khớp | 🆕 🧪 |
| M9 | Mọi đĩa phải dùng chung một thẻ nhớ (save đĩa 1 load được ở đĩa 2) | Chạy qua `.m3u` → một thẻ nhớ `<tên>.srm`, không có thẻ riêng từng đĩa | ✅ 🧪 |
| M10 | Game PS2 nhiều đĩa | LRPS2 hỗ trợ `.m3u` và đổi đĩa; thẻ nhớ dùng chung | 🆕 · cần thử với game thật |

## 4. Kiểm thử

### 4.1. Tự động — chạy mỗi lần build (`dotnet test`, CI trên Windows / macOS / Linux)

70 kiểm thử, trong đó các tình huống ở trên: nhận lại save khi ghép đĩa, game bản cũ chưa có lịch sử, `EBOOT.PBP` trùng tên (game mới / game đã chơi ở bản cũ), "chơi tiếp" không thấy state của game trùng tên, ROM băng trùng tên, sao lưu chỉ khi đổi + giữ 10 bản + khôi phục, thẻ nhớ PS2, state theo tên mới, đổi thư mục dữ liệu, thiếu đĩa, cấu hình bảo vệ save, ba kiểu sắp xếp Legend of Dragoon.

### 4.2. Đầu-cuối với RetroArch thật — chạy trước mỗi lần phát hành (Windows)

```
./tools/e2e-test.ps1 -AppDir C:\GameCenter
```

Dùng RetroArch và core trong thư mục cài, ảnh đĩa PS1 và ROM Game Boy **tự tạo** (không cần game thật), đường dẫn có dấu tiếng Việt. Kết quả lần chạy với RetroArch 1.22.2, Beetle PSX HW 0.9.44.1, Gambatte:

| Kiểm thử | Kết quả |
|---|---|
| Nạp `.m3u` 2 đĩa, core báo hỗ trợ đổi đĩa, đĩa trong khay 1/2 | ✅ |
| Thẻ nhớ `E2E Game.srm` (nhận lại từ `E2E Game (Disc 1).srm`) được RetroArch đọc và ghi lại nguyên vẹn; không sinh thẻ riêng cho đĩa 2 | ✅ |
| Thoát ở đĩa 2 (`.ldci`) → lần sau RetroArch mở lại đúng đĩa 2/2, cùng thẻ nhớ | ✅ |
| Sao lưu tự động trước khi chơi; chỗ đang chơi lưu theo tên playlist | ✅ |
| Máy băng: "Chơi tiếp" sau khi đã save trong game — cấu hình 0.2.0 **mất save** (SRAM về bản cũ), 0.3.0 giữ bản mới nhất | ✅ (lỗi 0.2.0 tái hiện được, đã sửa) |
| PS1: "Chơi tiếp" không kéo thẻ nhớ về quá khứ (Beetle không ghi đè thẻ nhớ khi nạp state; vẫn bật chặn để phòng) | ✅ |
| Hai game trùng tên file (PS1 `LOD.cue` bản Việt hóa và bản gốc; Game Boy `game.gb` ở hai thư mục): mỗi game giữ save và chỗ đang chơi riêng, RetroArch ghi đúng thư mục riêng, file game không đổi | ✅ |
| Không ghi file nào vào thư mục cài đặt (trước đây RetroArch ghi thời gian chơi `.lrtl` vào `Emulators/RetroArch/playlists/logs`; thư mục save riêng chưa tạo thì state rơi vào `Emulators/RetroArch/states`) | ✅ đã sửa cả hai |

### 4.3. Thủ công — checklist bắt buộc trước khi phát hành

Làm trên **máy Windows sạch** và **máy đang dùng bản 0.2.0 có save thật**:

- [ ] Nâng cấp từ 0.2.0: game cũ, yêu thích, lịch sử chơi còn nguyên; mở 3 game đã có save (PS1 `.cue`, PS1 `EBOOT.PBP`, SNES) → load được save trong game.
- [ ] Game nhiều đĩa thật (The Legend of Dragoon 4 đĩa, cả bản Việt hóa chia thư mục từng đĩa): save ở đĩa 1 → đổi đĩa qua menu → đĩa 2 load được save; thoát ở đĩa 2 → mở lại vào thẳng đĩa 2.
- [ ] Save trong game → thoát chọn "Không lưu" chỗ đang chơi → "Chơi tiếp" → vào menu load của game thấy save mới nhất.
- [ ] Rút điện / tắt RetroArch bằng Task Manager giữa game (sau khi save trong game > 10 giây) → save còn.
- [ ] "⋯ → Khôi phục save cũ" đưa được save về bản trước; đổi ý khôi phục lại bản vừa thay.
- [ ] Đổi thư mục dữ liệu sang ổ khác → mở lại → save có ở thư mục mới.
- [ ] Gỡ cài đặt rồi cài lại → Saves, States, BIOS, Backups còn nguyên.
- [ ] Đường dẫn cài và tên game có dấu tiếng Việt, có dấu cách.
- [ ] PS2 (có BIOS thật): save thẻ nhớ ở game A, game B vẫn thấy thẻ nhớ; game PS2 nhiều đĩa đổi đĩa được.
- [ ] Giao diện tiếng Anh và tiếng Việt: toàn bộ hộp thoại trên, menu RetroArch cùng ngôn ngữ.
- [ ] macOS / Linux: ít nhất khởi động, quét, chơi một game PS1 và một game băng (chưa có máy thật để thử — ghi rõ trong ghi chú phát hành nếu chưa làm).

## 5. Đề xuất tiếp theo

- 💡 Nút "Ghép đĩa" chọn tay (M6).
- 💡 RetroArch tạo thư mục rỗng `cheats/<core>` cạnh `retroarch.exe` khi chạy core (vô hại, không liên quan save); có thể chuyển `cheat_database_path` sang thư mục dữ liệu.
- 💡 Đọc log RetroArch sau mỗi lần chơi: nếu có lỗi ghi save (`[SRAM]` thất bại) thì báo ngay và giữ bản sao lưu.
- 💡 Kiểm tra dung lượng ổ trước khi chơi.
- 💡 Chạy `tools/e2e-test.ps1` trên GitHub Actions Windows (tải RetroArch + core như khi đóng gói) để kiểm thử đầu-cuối tự động mỗi lần đẩy tag.
