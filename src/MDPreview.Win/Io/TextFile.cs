using System.IO;
using System.Text;

namespace MDPreview.Win.Io;

/// <summary>
/// 文档读写：编码探测（BOM → UTF-8 严格 → GB18030）、换行风格保持、尾换行归一、原子写。
/// 这是磁盘写的唯一入口；内核只提供文本，不碰文件系统。
/// </summary>
public static class TextFile
{
    public sealed record TextDocument(string Text, Encoding Encoding, bool HasBom, string NewLine);

    static TextFile()
    {
        // 静态字段初始化在 cctor 体之前执行，提供程序注册放进 ResolveGb18030()；
        // 这里保留一次显式注册，供不使用 Gb18030 的调用路径。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>GB18030 优先，缺提供程序时退 GBK；都不可用则退 UTF-8（不应发生）。</summary>
    static readonly Encoding Gb18030 = ResolveGb18030();

    static Encoding ResolveGb18030()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var codePage in new[] { 54936, 936 })
        {
            try
            {
                return Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                Log.Warn($"codepage {codePage} unavailable: {ex.Message}");
            }
        }
        return new UTF8Encoding(false);
    }

    public static TextDocument Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, hasBom, offset) = Detect(bytes);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        return new TextDocument(NormalizeNewLines(text), encoding, hasBom, DetectNewLine(text));
    }

    public static void WriteAtomic(string path, string text, Encoding encoding, bool hasBom, string newLine)
    {
        var normalized = NormalizeNewLines(text);
        if (!normalized.EndsWith('\n')) normalized += "\n";
        var body = newLine == "\r\n" ? normalized.Replace("\n", "\r\n") : normalized;

        var bytes = encoding.GetBytes(body);
        if (hasBom)
        {
            var preamble = encoding.GetPreamble();
            if (preamble.Length > 0) bytes = [.. preamble, .. bytes];
            else if (encoding is UTF8Encoding) bytes = [0xEF, 0xBB, 0xBF, .. bytes];
        }

        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            // 同一卷内 Move 覆盖是原子的；失败时原文件保持不变
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static (Encoding Encoding, bool HasBom, int Offset) Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8, true, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, true, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, true, 2);

        try
        {
            StrictUtf8.GetString(bytes);
            return (StrictUtf8, false, 0);
        }
        catch (DecoderFallbackException)
        {
            return (Gb18030, false, 0);
        }
    }

    public static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string DetectNewLine(string text)
    {
        var crlf = text.Count(c => c == '\r');
        var lf = text.Count(c => c == '\n');
        return crlf > 0 && crlf * 2 >= lf ? "\r\n" : "\n";
    }
}
