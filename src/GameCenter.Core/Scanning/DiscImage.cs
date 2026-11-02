using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GameCenter.Core.Scanning;

public enum DiscKind
{
    /// <summary>Không phải ảnh đĩa ISO 9660, hoặc không đọc được.</summary>
    Unknown,
    Ps1,
    Ps2,
    Psp,
    /// <summary>Ảnh đĩa ISO 9660 của máy khác (Sega CD, Saturn, PC...).</summary>
    Other,
}

/// <summary>
/// Nhận diện ảnh đĩa theo nội dung (ISO 9660). Chỉ đọc vài sector, không đọc cả đĩa.
/// - PS1 và PS2 đều có system id "PLAYSTATION" ở Primary Volume Descriptor (sector 16);
///   khác nhau ở file SYSTEM.CNF: PS2 có dòng "BOOT2 = cdrom0:\...", PS1 có "BOOT = cdrom:\...".
/// - Đọc được sector 2048 byte (.iso đĩa DVD), sector thô 2352 byte (.bin đĩa CD, Mode 1 / Mode 2)
///   và ảnh nén .cso (CISO: từng khối nén deflate).
/// </summary>
public static partial class DiscImage
{
    public const int SectorSize = 2048;
    private const int RawSectorSize = 2352;
    /// <summary>Thư mục gốc đĩa game chỉ vài sector; giới hạn để không đọc lung tung khi file hỏng.</summary>
    private const int MaxDirSectors = 64;

    [GeneratedRegex(@"^\s*BOOT2\s*=", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Boot2();

    public static DiscKind Detect(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Detect(fs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DiscKind.Unknown; }
    }

    public static DiscKind Detect(Stream s)
    {
        try
        {
            var reader = SectorReader.Open(s);
            var pvd = reader?.Read(16);
            if (reader == null || pvd == null || pvd[0] != 1 || !Ascii(pvd, 1, "CD001")) return DiscKind.Unknown;

            var system = Encoding.ASCII.GetString(pvd, 8, 32).Trim(' ', '\0');
            if (system == "PSP GAME") return DiscKind.Psp;
            if (system != "PLAYSTATION") return DiscKind.Other;

            // Bản ghi thư mục gốc nằm ở byte 156 của PVD
            var cnf = ReadRootFile(reader, pvd.AsSpan(156, 34), "SYSTEM.CNF");
            // Vài game PS1 đời đầu không có SYSTEM.CNF (khởi động thẳng PSX.EXE); đĩa PS2 luôn có
            if (cnf == null) return DiscKind.Ps1;
            return Boot2().IsMatch(cnf) ? DiscKind.Ps2 : DiscKind.Ps1;
        }
        catch (InvalidDataException) { return DiscKind.Unknown; } // khối .cso hỏng
    }

    /// <summary>Mẫu đồng bộ ở đầu mỗi sector CD thô: 00 FF×10 00.</summary>
    public static bool HasCdSync(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12 || b[0] != 0 || b[11] != 0) return false;
        for (int i = 1; i <= 10; i++) if (b[i] != 0xFF) return false;
        return true;
    }

    private static bool Ascii(byte[] b, int offset, string text) =>
        b.Length >= offset + text.Length && Encoding.ASCII.GetString(b, offset, text.Length) == text;

    /// <summary>Đọc một file nhỏ ở thư mục gốc của đĩa (tối đa 2 sector), null nếu không có.</summary>
    private static string? ReadRootFile(SectorReader reader, ReadOnlySpan<byte> rootRecord, string name)
    {
        uint dirLba = BinaryPrimitives.ReadUInt32LittleEndian(rootRecord[2..]);
        uint dirSize = BinaryPrimitives.ReadUInt32LittleEndian(rootRecord[10..]);
        int sectors = (int)Math.Min((dirSize + SectorSize - 1) / SectorSize, MaxDirSectors);

        for (int i = 0; i < sectors; i++)
        {
            var sec = reader.Read(dirLba + i);
            if (sec == null) return null;
            int p = 0;
            while (p + 33 < SectorSize)
            {
                int len = sec[p];
                if (len == 0) break; // phần còn lại của sector để trống, bản ghi tiếp ở sector sau
                int nameLen = sec[p + 32];
                bool isDir = (sec[p + 25] & 0x02) != 0;
                if (!isDir && p + 33 + nameLen <= SectorSize)
                {
                    var entry = Encoding.ASCII.GetString(sec, p + 33, nameLen);
                    int semi = entry.IndexOf(';');
                    if (semi >= 0) entry = entry[..semi];
                    if (entry.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        uint lba = BinaryPrimitives.ReadUInt32LittleEndian(sec.AsSpan(p + 2));
                        int size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(sec.AsSpan(p + 10)), 2 * SectorSize);
                        var data = new byte[size];
                        for (int done = 0, k = 0; done < size; done += SectorSize, k++)
                        {
                            var part = reader.Read(lba + k);
                            if (part == null) return null;
                            Array.Copy(part, 0, data, done, Math.Min(SectorSize, size - done));
                        }
                        return Encoding.ASCII.GetString(data);
                    }
                }
                p += len;
            }
        }
        return null;
    }

    private static byte[]? ReadAt(Stream s, long offset, int count)
    {
        if (offset < 0 || offset + count > s.Length) return null;
        s.Position = offset;
        var buf = new byte[count];
        return s.ReadAtLeast(buf, count, throwOnEndOfStream: false) == count ? buf : null;
    }

    /// <summary>Đọc phần dữ liệu 2048 byte của một sector, theo kiểu file ảnh đĩa.</summary>
    private abstract class SectorReader
    {
        public abstract byte[]? Read(long lba);

        public static SectorReader? Open(Stream s)
        {
            var head = ReadAt(s, 0, 16);
            if (head == null) return null;
            if (Ascii(head, 0, "CISO")) return CsoReader.TryOpen(s);
            return HasCdSync(head) ? new RawReader(s) : new FlatReader(s);
        }
    }

    /// <summary>.iso: sector 2048 byte liền nhau.</summary>
    private sealed class FlatReader(Stream s) : SectorReader
    {
        public override byte[]? Read(long lba) => ReadAt(s, lba * SectorSize, SectorSize);
    }

    /// <summary>.bin: sector thô 2352 byte; byte 15 là mode. Mode 1: dữ liệu từ byte 16; Mode 2 (XA, PS1/PS2): từ byte 24.</summary>
    private sealed class RawReader(Stream s) : SectorReader
    {
        public override byte[]? Read(long lba)
        {
            var raw = ReadAt(s, lba * RawSectorSize, RawSectorSize);
            if (raw == null) return null;
            int offset = raw[15] == 1 ? 16 : 24;
            return raw.AsSpan(offset, SectorSize).ToArray();
        }
    }

    /// <summary>
    /// .cso (CISO): header 24 byte ("CISO", u32 header, u64 tổng dung lượng, u32 cỡ khối, u8 version, u8 align),
    /// sau đó là bảng chỉ mục u32 cho từng khối: bit cao = khối không nén, còn lại = vị trí &gt;&gt; align.
    /// Khối nén bằng deflate thô.
    /// </summary>
    private sealed class CsoReader : SectorReader
    {
        private readonly Stream _s;
        private readonly long _total;
        private readonly int _blockSize;
        private readonly int _align;

        private CsoReader(Stream s, long total, int blockSize, int align)
        {
            _s = s;
            _total = total;
            _blockSize = blockSize;
            _align = align;
        }

        public static CsoReader? TryOpen(Stream s)
        {
            var h = ReadAt(s, 0, 24);
            if (h == null) return null;
            long total = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(8));
            int blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16));
            if (blockSize < SectorSize || blockSize % SectorSize != 0 || blockSize > 1 << 20 || total <= 0) return null;
            return new CsoReader(s, total, blockSize, h[21]);
        }

        public override byte[]? Read(long lba)
        {
            long offset = lba * SectorSize;
            if (offset + SectorSize > _total) return null;
            long block = offset / _blockSize;
            var index = ReadAt(_s, 24 + block * 4, 8);
            if (index == null) return null;
            uint cur = BinaryPrimitives.ReadUInt32LittleEndian(index);
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(4));
            long pos = (long)(cur & 0x7FFFFFFF) << _align;
            long end = (long)(next & 0x7FFFFFFF) << _align;

            byte[] data;
            if ((cur & 0x80000000) != 0)
            {
                data = ReadAt(_s, pos, _blockSize) ?? Array.Empty<byte>();
            }
            else
            {
                var packed = ReadAt(_s, pos, (int)Math.Clamp(end - pos, 0, 2L * _blockSize));
                if (packed == null) return null;
                using var inflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
                data = new byte[_blockSize];
                if (inflate.ReadAtLeast(data, _blockSize, throwOnEndOfStream: false) < _blockSize) return null;
            }
            if (data.Length < _blockSize) return null;
            return data.AsSpan((int)(offset % _blockSize), SectorSize).ToArray();
        }
    }
}

public enum ChdMedia { Unknown, Cd, Dvd, Other }

/// <summary>
/// Đọc metadata file .chd (MAME Compressed Hunks of Data) để biết là đĩa CD hay DVD, không cần giải nén.
/// Đĩa DVD trong số các máy được hỗ trợ chỉ có PS2; đĩa CD thường là PS1 (game PS2 dạng CD thì dựa vào thư mục PS2).
/// </summary>
public static class ChdFile
{
    private const int MaxMetadataEntries = 64;

    public static ChdMedia ReadMedia(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ReadMedia(fs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ChdMedia.Unknown; }
    }

    public static ChdMedia ReadMedia(Stream s)
    {
        // Header: "MComprHD", u32 độ dài header, u32 version (big-endian).
        // Vị trí metadata đầu tiên: v3/v4 ở byte 36, v5 ở byte 48 (u64 big-endian).
        var h = new byte[64];
        s.Position = 0;
        if (s.ReadAtLeast(h, h.Length, throwOnEndOfStream: false) < 56 || Encoding.ASCII.GetString(h, 0, 8) != "MComprHD")
            return ChdMedia.Unknown;
        uint version = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(12));
        ulong meta = version switch
        {
            >= 5 => BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(48)),
            3 or 4 => BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(36)),
            _ => 0,
        };

        // Mỗi mục metadata: u32 thẻ, u32 (cờ + độ dài), u64 vị trí mục tiếp theo, rồi dữ liệu.
        var entry = new byte[16];
        for (int i = 0; meta != 0 && meta < (ulong)s.Length && i < MaxMetadataEntries; i++)
        {
            s.Position = (long)meta;
            if (s.ReadAtLeast(entry, 16, throwOnEndOfStream: false) < 16) break;
            switch (Encoding.ASCII.GetString(entry, 0, 4))
            {
                case "CHT2" or "CHTR" or "CHCD": return ChdMedia.Cd;
                case "DVD ": return ChdMedia.Dvd;
                case "CHGT" or "CHGD" or "GDDD": return ChdMedia.Other; // GD-ROM (Dreamcast), ổ cứng
            }
            meta = BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8));
        }
        return ChdMedia.Unknown;
    }
}
