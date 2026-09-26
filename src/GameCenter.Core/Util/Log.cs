namespace GameCenter.Core.Util;

/// <summary>Log đơn giản ghi vào Logs/gamecenter.log để hỗ trợ khi lỗi.</summary>
public static class Log
{
    private static readonly object Lock = new();
    private static string? _file;

    public static void Init(string logFile)
    {
        _file = logFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            // Giữ log gọn: quá 2 MB thì đổi tên sang .old
            var fi = new FileInfo(logFile);
            if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                File.Move(logFile, logFile + ".old", overwrite: true);
        }
        catch { }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}\n{ex}");

    private static void Write(string level, string msg)
    {
        if (_file == null) return;
        try
        {
            lock (Lock)
                File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
