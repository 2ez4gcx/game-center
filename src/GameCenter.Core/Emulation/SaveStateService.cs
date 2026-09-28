namespace GameCenter.Core.Emulation;

/// <summary>
/// Save state tự động của RetroArch ("&lt;tên game&gt;.state.auto" trong States/).
/// Game Center hỏi người chơi khi thoát có muốn giữ không; không liên quan tới save trong game (.srm).
/// </summary>
public sealed class SaveStateService
{
    public const string AutoSuffix = ".state.auto";
    private const string BackupSuffix = ".gcbak";
    private readonly string _statesDir;

    public SaveStateService(string statesDir) => _statesDir = statesDir;

    /// <summary>Tên RetroArch dùng để đặt save: tên file chạy, bỏ đuôi (với "a.zip#rom.nes" là "rom").</summary>
    public static string ContentName(string launchFile)
    {
        int hash = launchFile.IndexOf(".zip#", StringComparison.OrdinalIgnoreCase);
        var file = hash >= 0 ? launchFile[(hash + 5)..] : launchFile;
        // Chấp nhận cả "\" và "/" để kết quả giống nhau trên mọi hệ điều hành
        int slash = file.LastIndexOfAny(new[] { '\\', '/' });
        return Path.GetFileNameWithoutExtension(slash >= 0 ? file[(slash + 1)..] : file);
    }

    /// <summary>File save state tự động của game (tìm trong mọi thư mục con theo core).</summary>
    public string? FindAutoState(string launchFile)
    {
        if (!Directory.Exists(_statesDir)) return null;
        var name = ContentName(launchFile) + AutoSuffix;
        return Directory.EnumerateFiles(_statesDir, "*" + AutoSuffix, SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>Trước khi chơi: sao lưu bản cũ để khôi phục nếu người chơi chọn "Không lưu".</summary>
    public string? BackupBeforePlay(string launchFile)
    {
        var state = FindAutoState(launchFile);
        if (state == null) return null;
        File.Copy(state, state + BackupSuffix, overwrite: true);
        return state;
    }

    /// <summary>Bản save state được tạo/ghi đè trong lần chơi vừa rồi (null nếu không có).</summary>
    public string? NewStateSince(string launchFile, DateTime startedUtc)
    {
        var state = FindAutoState(launchFile);
        return state != null && File.GetLastWriteTimeUtc(state) >= startedUtc.AddSeconds(-2) ? state : null;
    }

    /// <summary>Người chơi chọn lưu: giữ bản mới, bỏ bản sao lưu.</summary>
    public static void Keep(string state)
    {
        TryDelete(state + BackupSuffix);
    }

    /// <summary>Người chơi chọn không lưu: xóa bản mới, khôi phục bản cũ nếu có.</summary>
    public static void Discard(string state)
    {
        var backup = state + BackupSuffix;
        TryDelete(state + ".png");
        if (File.Exists(backup)) File.Move(backup, state, overwrite: true);
        else TryDelete(state);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
