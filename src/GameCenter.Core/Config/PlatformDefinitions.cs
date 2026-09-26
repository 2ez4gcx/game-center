using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameCenter.Core.Config;

public sealed class BiosInfo
{
    public List<string> Files { get; set; } = new();
    /// <summary>true: không có BIOS thì không chạy được. false: core có BIOS thay thế (OpenBIOS/HLE).</summary>
    public bool Required { get; set; }
    public string? Note { get; set; }
}

public sealed class PlatformDefinition
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Folders { get; set; } = new();
    public string Core { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public BiosInfo? Bios { get; set; }
}

public sealed class PlatformCatalog
{
    [JsonPropertyName("platforms")]
    public List<PlatformDefinition> Platforms { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PlatformCatalog Load(string path)
    {
        var catalog = JsonSerializer.Deserialize<PlatformCatalog>(File.ReadAllText(path), JsonOptions)
                      ?? throw new InvalidDataException($"Không đọc được {path}");
        return catalog;
    }

    public PlatformDefinition? Get(string name) =>
        Platforms.FirstOrDefault(p => p.Enabled && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tìm platform theo tên thư mục (ví dụ "Games/SNES/...").</summary>
    public PlatformDefinition? FromFolderName(string folder) =>
        Platforms.FirstOrDefault(p => p.Enabled &&
            (p.Name.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
             p.Folders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase))));

    /// <summary>
    /// Platform duy nhất nhận extension này (bỏ qua .zip và .bin vì dùng chung).
    /// </summary>
    public PlatformDefinition? UniqueForExtension(string ext)
    {
        var matches = Platforms.Where(p => p.Enabled && p.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public bool IsKnownExtension(string ext) =>
        Platforms.Any(p => p.Enabled && p.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
}
