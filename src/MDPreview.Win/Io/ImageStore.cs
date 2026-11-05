using System.IO;
using System.Text;

namespace MDPreview.Win.Io;

/// <summary>一张已落盘的图片：绝对路径 + 文件名（相对 resource 目录）。</summary>
public sealed record ImportedImage(string Path, string FileName);

/// <summary>导入失败的原因（壳负责翻成提示语，内核只拿到 ok:false）。</summary>
public sealed class ImageImportException(string message) : Exception(message);

/// <summary>
/// 图片落盘（对齐上游 ImageStore 的目录与命名约定）：
/// <c>&lt;文档目录&gt;/resource/&lt;文档名去扩展&gt;/</c>，剪贴板位图命名 <c>img_NNN.ext</c>，
/// 既有文件保留原名、冲突追加 <c>-2</c>、<c>-3</c>。
/// 目录与命名只在这里定义，调用方只拿结果路径。
/// </summary>
public static class ImageStore
{
    /// <summary>web 侧预筛用的扩展名；壳侧以 <see cref="IsImageFile"/> 为权威。</summary>
    public static readonly string[] KnownExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".tif", ".tiff", ".heic", ".heif", ".avif"];

    /// <summary>文档旁的图片资源目录：<c>&lt;docDir&gt;/resource/&lt;docBaseName&gt;</c>。</summary>
    public static string DirectoryFor(string docPath)
    {
        var docDir = System.IO.Path.GetDirectoryName(docPath)
                     ?? throw new ImageImportException("文档还没有保存位置，无法插入图片");
        var stem = System.IO.Path.GetFileNameWithoutExtension(docPath);
        return System.IO.Path.Combine(docDir, "resource", stem);
    }

    /// <summary>剪贴板位图：按 mime 定扩展名（缺省 png），序号命名。</summary>
    public static ImportedImage ImportBlob(byte[] data, string? mime, string docPath)
    {
        if (data.Length == 0) throw new ImageImportException("剪贴板里没有图片数据");
        var ext = ExtensionForMime(mime) ?? "png";
        var dir = DirectoryFor(docPath);
        Directory.CreateDirectory(dir);

        var name = NextSequenceName(ext, dir);
        var target = System.IO.Path.Combine(dir, name);
        WriteAtomicBytes(target, data);
        return new ImportedImage(target, name);
    }

    /// <summary>既有图片文件（拖入/菜单）：校验是图片后复制，保留原名并在冲突时加序号。</summary>
    public static ImportedImage ImportFile(string sourcePath, string docPath)
    {
        var source = sourcePath.Trim();
        if (source.Length == 0) throw new ImageImportException("没有可导入的图片路径");
        if (!File.Exists(source)) throw new ImageImportException($"找不到图片文件：{source}");
        if (!IsImageFile(source)) throw new ImageImportException("不是可识别的图片文件");

        var dir = DirectoryFor(docPath);
        Directory.CreateDirectory(dir);

        var name = UniqueName(System.IO.Path.GetFileName(source), dir);
        var target = System.IO.Path.Combine(dir, name);
        File.Copy(source, target, overwrite: false);
        return new ImportedImage(target, name);
    }

    /// <summary>
    /// 关闭时清理：删掉引用集合之外的图片，空目录逐级回收。
    /// 只处理 image 扩展名，其它文件（将来的附件、用户手放的杂物）一律不动。
    /// </summary>
    /// <returns>被删除的文件名（供日志/自检断言）。</returns>
    public static IReadOnlyList<string> CleanUnreferenced(string docPath, IEnumerable<string> referencedPaths)
    {
        var dir = DirectoryFor(docPath);
        if (!Directory.Exists(dir)) return [];

        var referenced = new HashSet<string>(
            referencedPaths.Select(p => Normalize(p)),
            StringComparer.OrdinalIgnoreCase);

        var deleted = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (!IsImageFile(file)) continue;
            if (referenced.Contains(Normalize(file))) continue;
            try
            {
                File.Delete(file);
                deleted.Add(System.IO.Path.GetFileName(file));
            }
            catch (IOException ex)
            {
                Log.Warn($"image gc failed for {file}: {ex.Message}");
            }
        }

        TryRemoveEmpty(dir);
        TryRemoveEmpty(System.IO.Path.GetDirectoryName(dir)!);
        return deleted;
    }

    public static bool IsImageFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (!KnownExtensions.Contains(ext)) return false;
        try { return File.Exists(path); } catch { return false; }
    }

    public static string? ExtensionForMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime)) return null;
        return mime.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            "image/tiff" => "tiff",
            "image/avif" => "avif",
            "image/heic" => "heic",
            "image/heif" => "heif",
            _ => null,
        };
    }

    /// <summary>剪贴板位图命名：<c>img_</c> 最大序号 +1，三位补零。</summary>
    static string NextSequenceName(string ext, string dir)
    {
        var max = 0;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = System.IO.Path.GetFileName(file);
            if (!name.StartsWith("img_", StringComparison.OrdinalIgnoreCase)) continue;
            var dot = name.IndexOf('.');
            if (dot <= 4) continue;
            if (int.TryParse(name[4..dot], out var seq) && seq > max) max = seq;
        }
        return $"img_{max + 1:D3}.{ext}";
    }

    /// <summary>既有文件命名：占用则 <c>stem-2.ext</c>、<c>stem-3.ext</c>…</summary>
    static string UniqueName(string prefer, string dir)
    {
        if (!File.Exists(System.IO.Path.Combine(dir, prefer))) return prefer;

        var stem = System.IO.Path.GetFileNameWithoutExtension(prefer);
        var ext = System.IO.Path.GetExtension(prefer);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}{ext}";
            if (!File.Exists(System.IO.Path.Combine(dir, candidate))) return candidate;
        }
    }

    static void WriteAtomicBytes(string target, byte[] data)
    {
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, target, overwrite: true);
    }

    static void TryRemoveEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path.TrimEnd('\\', '/'); }
    }

    /// <summary>把落盘路径转成内核要求的资源 URL（同源前缀 + 逐段编码）。</summary>
    public static string ToResourceUrl(string path)
    {
        var posix = path.Replace('\\', '/');
        var encoded = string.Join('/', posix.Split('/').Select(Uri.EscapeDataString));
        return $"{AppInfo.ResourcePrefix}/{encoded}";
    }

    /// <summary>文本形态的图片路径（file:// URL / 绝对路径 / ~/ 相对）→ 本地路径。</summary>
    public static string PathFromText(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(text).LocalPath; } catch { /* 落到下面按裸路径处理 */ }
        }
        if (text.StartsWith("~/", StringComparison.Ordinal))
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[2..]);
        return text;
    }

    /// <summary>把失败原因翻成用户提示（中文，与上游提示语同口径）。</summary>
    public static string DescribeFailure(Exception error) => error switch
    {
        ImageImportException known => known.Message,
        UnauthorizedAccessException => "没有写入权限：" + error.Message,
        IOException => "写入图片失败：" + error.Message,
        _ => "导入图片失败：" + error.Message,
    };
}
