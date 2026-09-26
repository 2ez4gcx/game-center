namespace GameCenter.Core.Util;

/// <summary>CRC32 (IEEE) dùng để nhận diện ROM theo hash (No-Intro dùng CRC32).</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(Stream s)
    {
        uint crc = 0xFFFFFFFFu;
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i++) crc = Table[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    public static string ComputeHex(Stream s) => Compute(s).ToString("X8");
}
