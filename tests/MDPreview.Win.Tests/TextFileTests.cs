using System.IO;
using System.Text;
using MDPreview.Win.Io;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>文档读写：编码探测、换行保持、尾换行归一、原子写。</summary>
public sealed class TextFileTests : IDisposable
{
    readonly string _dir = TestBootstrap.NewCaseDirectory("textfile-" + Guid.NewGuid().ToString("N"));

    public TextFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    string PathOf(string name) => Path.Combine(_dir, name);

    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Read_utf8_without_bom_keeps_text_and_lf()
    {
        var path = PathOf("plain.md");
        File.WriteAllBytes(path, Utf8NoBom.GetBytes("# 标题\n\n正文\n"));

        var doc = TextFile.Read(path);

        Assert.Equal("# 标题\n\n正文\n", doc.Text);
        Assert.False(doc.HasBom);
        Assert.Equal("\n", doc.NewLine);
        Assert.Equal(65001, doc.Encoding.CodePage);
    }

    [Fact]
    public void Read_utf8_with_bom_detects_bom_and_strips_it()
    {
        var path = PathOf("bom.md");
        var bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllBytes(path, [.. bom.GetPreamble(), .. bom.GetBytes("带 BOM\n")]);

        var doc = TextFile.Read(path);

        Assert.True(doc.HasBom);
        Assert.Equal("带 BOM\n", doc.Text);
    }

    [Fact]
    public void Read_crlf_normalizes_text_but_remembers_style()
    {
        var path = PathOf("crlf.md");
        File.WriteAllBytes(path, Utf8NoBom.GetBytes("第一行\r\n第二行\r\n"));

        var doc = TextFile.Read(path);

        Assert.Equal("第一行\n第二行\n", doc.Text);
        Assert.Equal("\r\n", doc.NewLine);
    }

    [Fact]
    public void Read_gb18030_legacy_file()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb = Encoding.GetEncoding(54936);
        var path = PathOf("gb.md");
        File.WriteAllBytes(path, gb.GetBytes("中文内容，GB18030 编码。\n"));

        var doc = TextFile.Read(path);

        Assert.Equal("中文内容，GB18030 编码。\n", doc.Text);
        Assert.Contains(doc.Encoding.CodePage, new[] { 54936, 936 });
    }

    [Fact]
    public void WriteAtomic_appends_trailing_newline_and_overwrites()
    {
        var path = PathOf("write.md");
        File.WriteAllText(path, "旧内容");

        TextFile.WriteAtomic(path, "新内容", Utf8NoBom, hasBom: false, newLine: "\n");

        Assert.Equal("新内容\n", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp*"));
    }

    [Fact]
    public void WriteAtomic_preserves_crlf_style()
    {
        var path = PathOf("crlf-out.md");

        TextFile.WriteAtomic(path, "a\nb\n", Utf8NoBom, hasBom: false, newLine: "\r\n");

        Assert.Equal("a\r\nb\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAtomic_preserves_bom_when_asked()
    {
        var path = PathOf("bom-out.md");
        var bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        TextFile.WriteAtomic(path, "x\n", bom, hasBom: true, newLine: "\n");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(bom.GetPreamble(), bytes[..3]);
        Assert.Equal("x\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Roundtrip_read_then_write_is_stable()
    {
        var path = PathOf("roundtrip.md");
        File.WriteAllBytes(path, Utf8NoBom.GetBytes("# A\n\n- 一\n- 二\n"));

        var doc = TextFile.Read(path);
        TextFile.WriteAtomic(path, doc.Text, doc.Encoding, doc.HasBom, doc.NewLine);

        Assert.Equal("# A\n\n- 一\n- 二\n", File.ReadAllText(path));
    }
}
