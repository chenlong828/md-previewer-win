using System.IO;
using System.Text.Json;

namespace MDPreview.Win.Io;

/// <summary>一门语言的展示名与扩展名（来自 `resources/languages.json`，唯一来源）。</summary>
public sealed record CodeLanguage(string Id, string DisplayName, IReadOnlyList<string> Extensions);

/// <summary>
/// 代码语言的唯一来源：`resources/languages.json`（与上游 FileSupport.codeLanguages 同口径）。
/// C# 只做「扩展名 → 语言 id」映射，不手抄清单；内核侧用同一份 id 交给 refractor。
/// </summary>
public static class CodeLanguages
{
    public const string PlainText = "plaintext";
    public const string Auto = "auto";

    static readonly Lazy<IReadOnlyList<CodeLanguage>> LazyAll = new(LoadCore);

    public static IReadOnlyList<CodeLanguage> All => LazyAll.Value;

    /// <summary>扩展名（小写、不带点）→ 语言 id。</summary>
    public static string? IdForExtension(string extension)
    {
        var key = extension.TrimStart('.').ToLowerInvariant();
        if (key.Length == 0) return null;
        foreach (var language in All)
        {
            foreach (var ext in language.Extensions)
            {
                if (string.Equals(ext, key, StringComparison.OrdinalIgnoreCase)) return language.Id;
            }
        }
        return null;
    }

    /// <summary>文件路径 → 语言 id；不认识则 <see cref="PlainText"/>。</summary>
    public static string IdForPath(string path) =>
        IdForExtension(Path.GetExtension(path)) ?? PlainText;

    public static string DisplayName(string id)
    {
        foreach (var language in All)
            if (language.Id == id) return language.DisplayName;
        return id == PlainText ? "纯文本" : id;
    }

    /// <summary>清单文件位置：随包的 `resources/languages.json`（绿色目录里也在 resources/ 下）。</summary>
    public static string ListPath => Path.Combine(AppInfo.ResourcesDirectory, "languages.json");

    static IReadOnlyList<CodeLanguage> LoadCore()
    {
        try
        {
            if (File.Exists(ListPath))
            {
                var json = File.ReadAllText(ListPath);
                var parsed = JsonSerializer.Deserialize<List<CodeLanguage>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (parsed is { Count: > 0 }) return parsed;
            }
            Log.Warn($"languages.json missing or empty at {ListPath}; falling back to plain text");
        }
        catch (Exception ex)
        {
            Log.Warn($"languages.json load failed: {ex.Message}");
        }
        return [];
    }
}
