using System.Diagnostics;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Localization;
using GameCenter.Core.Scanning;
using GameCenter.Core.Util;

namespace GameCenter.Core.Emulation;

public enum LaunchProblem
{
    None,
    GameFileMissing,
    BrokenCue,
    UnknownPlatform,
    RetroArchMissing,
    CoreMissing,
    BiosMissing,
    DuckStationMissing,
}

public sealed record LaunchPlan(LaunchProblem Problem, string Message, ProcessStartInfo? StartInfo = null);

/// <summary>Chọn core, tạo command line và khởi chạy RetroArch (mục 10).</summary>
public sealed class EmulatorLauncher
{
    private readonly AppPaths _paths;
    private readonly PlatformCatalog _catalog;
    private readonly AppSettings _settings;

    public EmulatorLauncher(AppPaths paths, PlatformCatalog catalog, AppSettings settings)
    {
        _paths = paths;
        _catalog = catalog;
        _settings = settings;
    }

    public LaunchPlan Plan(GameRecord game)
    {
        if (game.ScanStatus == ScanStatus.BrokenCue)
            return new(LaunchProblem.BrokenCue, Loc.T("launch.broken", game.ScanMessage));
        if (!File.Exists(GameScanner.PhysicalPath(game.LaunchFile)))
            return new(LaunchProblem.GameFileMissing, Loc.T("launch.file_missing"));

        var platform = _catalog.Get(game.Platform);
        if (platform == null)
            return new(LaunchProblem.UnknownPlatform, Loc.T("launch.unknown_platform"));

        // DuckStation do người dùng tự cài (không đóng gói, mục 10.3)
        if (platform.Name == "PS1" && _settings.UseUserDuckStation)
        {
            if (string.IsNullOrWhiteSpace(_settings.DuckStationPath) || !File.Exists(_settings.DuckStationPath))
                return new(LaunchProblem.DuckStationMissing, Loc.T("launch.duck_missing"));
            var dsi = new ProcessStartInfo(_settings.DuckStationPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_settings.DuckStationPath)! };
            if (_settings.Fullscreen) dsi.ArgumentList.Add("-fullscreen");
            dsi.ArgumentList.Add("--");
            dsi.ArgumentList.Add(game.LaunchFile);
            return new(LaunchProblem.None, "", dsi);
        }

        if (!File.Exists(_paths.RetroArchExe))
            return new(LaunchProblem.RetroArchMissing, Loc.T("launch.retroarch_missing"));

        var core = _paths.CorePath(platform.Core);
        if (!File.Exists(core))
            return new(LaunchProblem.CoreMissing, Loc.T("launch.core_missing", platform.DisplayName, Path.GetFileName(core)));

        try { BiosChecker.Prepare(platform, _paths.BiosDir); }
        catch (Exception ex) { Log.Error($"Không chuẩn bị được BIOS {platform.Name}", ex); }
        if (BiosChecker.Check(platform, _paths.BiosDir) == BiosState.Missing)
            return new(LaunchProblem.BiosMissing, BiosMissingMessage(platform));

        return new(LaunchProblem.None, "", BuildRetroArchStartInfo(core, game.LaunchFile));
    }

    /// <summary>Hướng dẫn riêng từng hệ máy nếu có (khóa "launch.bios_missing.&lt;hệ máy&gt;"), không thì câu chung.</summary>
    public static string BiosMissingMessage(PlatformDefinition platform)
    {
        var key = "launch.bios_missing." + platform.Name;
        return Loc.T(Loc.Current.Has(key) ? key : "launch.bios_missing");
    }

    /// <summary>Dùng ArgumentList (từng tham số riêng) để an toàn với dấu cách và tiếng Việt.</summary>
    public ProcessStartInfo BuildRetroArchStartInfo(string corePath, string launchFile)
    {
        var psi = new ProcessStartInfo(_paths.RetroArchExe)
        {
            UseShellExecute = false,
            WorkingDirectory = _paths.RetroArchDir,
        };
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(_paths.RetroArchCfg);
        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add(corePath);
        psi.ArgumentList.Add(launchFile);
        return psi;
    }

    /// <summary>Chạy game và chờ đến khi thoát.</summary>
    public async Task<int> RunAsync(ProcessStartInfo psi, CancellationToken ct = default)
    {
        Log.Info($"Chạy: {psi.FileName} {string.Join(" ", psi.ArgumentList.Select(a => $"\"{a}\""))}");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi chạy được emulator.");
        await p.WaitForExitAsync(ct);
        Log.Info($"Emulator thoát, mã {p.ExitCode}");
        return p.ExitCode;
    }

    /// <summary>Mở RetroArch không kèm game (để cấu hình tay cầm).</summary>
    public ProcessStartInfo? BuildMenuStartInfo()
    {
        if (!File.Exists(_paths.RetroArchExe)) return null;
        var psi = new ProcessStartInfo(_paths.RetroArchExe) { UseShellExecute = false, WorkingDirectory = _paths.RetroArchDir };
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(_paths.RetroArchCfg);
        psi.ArgumentList.Add("--menu");
        return psi;
    }
}
