using System.Text;

namespace GameCenter.Core.Scanning;

/// <summary>
/// Nhận diện ROM băng (cartridge) theo header, không phụ thuộc đuôi file
/// (dùng cho .bin / .rom / file trong .zip đặt đuôi lạ). Chỉ cần <see cref="HeadSize"/> byte đầu file.
/// Trả về tên hệ máy: tên trong platforms.json nếu Game Center hỗ trợ, hoặc tên máy (để báo "chưa hỗ trợ").
/// </summary>
public static class RomHeader
{
    /// <summary>Đủ để đọc header Master System / Game Gear ở 0x7FF0.</summary>
    public const int HeadSize = 0x8000;

    public const string Nes = "NES", Gb = "GB", Gbc = "GBC", Gba = "GBA", MegaDrive = "MegaDrive",
        MasterSystem = "MasterSystem", GameGear = "GameGear";

    /// <summary>Máy chưa hỗ trợ nhưng nhận ra được (tên riêng, không dịch).</summary>
    public const string N64 = "Nintendo 64", Sega32X = "Sega 32X", Nds = "Nintendo DS";

    public static string? Detect(byte[] h)
    {
        // NES (iNES / NES 2.0): "NES" + 0x1A ở đầu file
        if (Ascii(h, 0, "NES\u001A")) return Nes;
        // Famicom Disk System: "FDS" + 0x1A, hoặc "*NINTENDO-HVC*" ở byte 1
        // (FCEUmm chạy được nhưng cần BIOS disksys.rom → coi như máy riêng, chưa hỗ trợ)
        if (Ascii(h, 0, "FDS\u001A") || Ascii(h, 1, "*NINTENDO-HVC*")) return "Famicom Disk System";
        if (IsGba(h)) return Gba;
        if (IsGb(h))
        {
            // Cờ CGB ở 0x143: 0x80 = chạy được cả GB lẫn GBC, 0xC0 = chỉ GBC. No-Intro xếp cả hai vào GBC.
            return (h[0x143] & 0x80) != 0 ? Gbc : Gb;
        }
        if (IsNds(h)) return Nds;
        if (Ascii(h, 0x100, "SEGA 32X") || Ascii(h, 0x101, "SEGA 32X")) return Sega32X;
        if (Ascii(h, 0x100, "SEGA") || Ascii(h, 0x101, "SEGA")) return MegaDrive;
        if (N64Magic(h)) return N64;
        // Master System / Game Gear: "TMR SEGA" ở 0x7FF0 (ROM nhỏ: 0x3FF0, 0x1FF0);
        // nửa cao byte cuối header là mã vùng: 3, 4 = Master System; 5, 6, 7 = Game Gear
        foreach (var at in new[] { 0x7FF0, 0x3FF0, 0x1FF0 })
        {
            if (!Ascii(h, at, "TMR SEGA")) continue;
            int region = h[at + 0xF] >> 4;
            return region is >= 5 and <= 7 ? GameGear : MasterSystem;
        }
        return null;
    }

    /// <summary>Header Game Boy: logo Nintendo ở 0x104 và tổng kiểm tra header ở 0x14D.</summary>
    private static bool IsGb(byte[] h)
    {
        if (h.Length < 0x150) return false;
        ReadOnlySpan<byte> logo = new byte[] { 0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B };
        if (!h.AsSpan(0x104, logo.Length).SequenceEqual(logo)) return false;
        byte x = 0;
        for (int i = 0x134; i <= 0x14C; i++) x = (byte)(x - h[i] - 1);
        return x == h[0x14D];
    }

    /// <summary>Header GBA: giá trị cố định 0x96 ở 0xB2 và byte bù ở 0xBD.</summary>
    private static bool IsGba(byte[] h)
    {
        if (h.Length < 0xC0 || h[0xB2] != 0x96) return false;
        byte chk = 0;
        for (int i = 0xA0; i <= 0xBC; i++) chk = (byte)(chk - h[i]);
        return (byte)(chk - 0x19) == h[0xBD];
    }

    /// <summary>Header Nintendo DS: CRC16 của logo ở 0x15C luôn là 0xCF56.</summary>
    private static bool IsNds(byte[] h) => h.Length >= 0x160 && h[0x15C] == 0x56 && h[0x15D] == 0xCF;

    /// <summary>ROM N64 ở 3 thứ tự byte: .z64 (80 37 12 40), .v64 (37 80 40 12), .n64 (40 12 37 80).</summary>
    private static bool N64Magic(byte[] h) => h.Length >= 4 && (
        (h[0] == 0x80 && h[1] == 0x37 && h[2] == 0x12 && h[3] == 0x40) ||
        (h[0] == 0x37 && h[1] == 0x80 && h[2] == 0x40 && h[3] == 0x12) ||
        (h[0] == 0x40 && h[1] == 0x12 && h[2] == 0x37 && h[3] == 0x80));

    private static bool Ascii(byte[] b, int offset, string text) =>
        b.Length >= offset + text.Length && Encoding.ASCII.GetString(b, offset, text.Length) == text;
}

/// <summary>
/// File Game Center chưa chạy được nhưng nhận ra được, để báo rõ cho người dùng thay vì bỏ qua im lặng:
/// file nén cần giải nén trước, và game của máy chưa hỗ trợ.
/// </summary>
public static class KnownFormats
{
    public enum Kind { None, Archive, MultiPart, Ecm, Unsupported }

    /// <summary>File nén RetroArch / core không mở trực tiếp được (hoặc chứa ảnh đĩa cần file thật).</summary>
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { ".7z", ".rar", ".gz", ".tgz", ".tar", ".xz", ".bz2", ".zst", ".lzh", ".cab" };

    /// <summary>Đuôi file game của máy Game Center chưa hỗ trợ → tên máy.</summary>
    private static readonly Dictionary<string, string> Unsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        [".n64"] = RomHeader.N64, [".z64"] = RomHeader.N64, [".v64"] = RomHeader.N64,
        [".nds"] = RomHeader.Nds, [".dsi"] = RomHeader.Nds,
        [".3ds"] = "Nintendo 3DS", [".cia"] = "Nintendo 3DS", [".cci"] = "Nintendo 3DS",
        [".gcm"] = "GameCube / Wii", [".gcz"] = "GameCube / Wii", [".rvz"] = "GameCube / Wii",
        [".wia"] = "GameCube / Wii", [".wbfs"] = "GameCube / Wii", [".wad"] = "GameCube / Wii",
        [".xci"] = "Nintendo Switch", [".nsp"] = "Nintendo Switch", [".nsz"] = "Nintendo Switch", [".xcz"] = "Nintendo Switch",
        [".fds"] = "Famicom Disk System",
        [".gdi"] = "Dreamcast", [".cdi"] = "Dreamcast",
        [".32x"] = RomHeader.Sega32X,
        [".pce"] = "PC Engine", [".sgx"] = "PC Engine SuperGrafx",
        [".a26"] = "Atari 2600", [".a52"] = "Atari 5200", [".a78"] = "Atari 7800", [".lnx"] = "Atari Lynx", [".j64"] = "Atari Jaguar",
        [".ws"] = "WonderSwan", [".wsc"] = "WonderSwan Color",
        [".ngp"] = "Neo Geo Pocket", [".ngc"] = "Neo Geo Pocket Color",
        [".vb"] = "Virtual Boy", [".col"] = "ColecoVision", [".int"] = "Intellivision",
        [".vpk"] = "PlayStation Vita", [".pkg"] = "PlayStation 3",
    };

    public static Kind Classify(string fileName, out string? console)
    {
        console = null;
        var lower = fileName.ToLowerInvariant();
        var ext = Path.GetExtension(lower);
        if (IsMultiPart(lower)) return Kind.MultiPart;
        if (Archives.Contains(ext)) return Kind.Archive;
        if (ext == ".ecm") return Kind.Ecm;
        if (Unsupported.TryGetValue(ext, out console)) return Kind.Unsupported;
        return Kind.None;
    }

    /// <summary>
    /// File nén chia nhiều phần: chỉ báo phần đầu (.001, .part1.rar), các phần sau bỏ qua cho đỡ rối.
    /// </summary>
    private static bool IsMultiPart(string lower) =>
        lower.EndsWith(".001") || System.Text.RegularExpressions.Regex.IsMatch(lower, @"\.part0*1\.rar$");

    /// <summary>
    /// Phần thứ 2 trở đi của file nén nhiều phần (.002, .r00, .z01, .part2.rar): không báo.
    /// .z64 là ROM Nintendo 64, không phải phần của zip chia nhỏ.
    /// </summary>
    public static bool IsLaterPart(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(lower, @"(\.(0(0[2-9]|[1-9]\d)|[1-9]\d\d)|\.r\d\d|\.z(?!64$)\d\d|\.part(0*[2-9]|0*[1-9]\d+)\.rar)$");
    }

    /// <summary>Chữ ký file nén khác zip (dùng khi file đuôi .zip nhưng thật ra là 7z / rar).</summary>
    public static bool IsOtherArchive(byte[] head) =>
        (head.Length >= 6 && head[0] == '7' && head[1] == 'z' && head[2] == 0xBC && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C) ||
        (head.Length >= 6 && Encoding.ASCII.GetString(head, 0, 4) == "Rar!" && head[4] == 0x1A && head[5] == 0x07);
}
