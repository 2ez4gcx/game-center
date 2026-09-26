using GameCenter.Core.Config;

namespace GameCenter.Core.Emulation;

public enum BiosState
{
    /// <summary>Platform không cần BIOS.</summary>
    NotNeeded,
    Present,
    /// <summary>Thiếu BIOS nhưng core có thay thế (OpenBIOS/HLE) → vẫn chạy.</summary>
    UsingFallback,
    /// <summary>Không thể chạy → hiển thị hướng dẫn.</summary>
    Missing,
}

/// <summary>Luồng kiểm tra BIOS (mục 11). Game Center không bao giờ đóng gói hoặc tải BIOS.</summary>
public static class BiosChecker
{
    public static BiosState Check(PlatformDefinition platform, string biosDir)
    {
        if (platform.Bios == null || platform.Bios.Files.Count == 0) return BiosState.NotNeeded;
        bool present = Directory.Exists(biosDir) && Directory.EnumerateFiles(biosDir)
            .Any(f => platform.Bios.Files.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        if (present) return BiosState.Present;
        return platform.Bios.Required ? BiosState.Missing : BiosState.UsingFallback;
    }
}
