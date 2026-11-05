using System.IO;
using System.Text;
using MDPreview.Win.Io;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>图片落盘：目录/命名约定、冲突、校验、GC、路径与 URL 编解码。</summary>
public sealed class ImageStoreTests : IDisposable
{
    readonly string _dir = TestBootstrap.NewCaseDirectory("images-" + Guid.NewGuid().ToString("N"));
    readonly string _doc;

    public ImageStoreTests()
    {
        _doc = Path.Combine(_dir, "笔记.md");
        File.WriteAllText(_doc, "# 笔记\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    string ResourceDir => Path.Combine(_dir, "resource", "笔记");

    [Fact]
    public void ImportBlob_uses_resource_subdir_and_sequence_name()
    {
        var first = ImageStore.ImportBlob(Png(), "image/png", _doc);
        var second = ImageStore.ImportBlob(Png(), "image/jpeg", _doc);

        Assert.Equal(ResourceDir, Path.GetDirectoryName(first.Path));
        Assert.Equal("img_001.png", first.FileName);
        Assert.Equal("img_002.jpg", second.FileName);
        Assert.True(File.Exists(first.Path));
    }

    [Fact]
    public void ImportBlob_defaults_to_png_for_unknown_mime()
    {
        var imported = ImageStore.ImportBlob(Png(), "application/octet-stream", _doc);
        Assert.EndsWith(".png", imported.FileName);
    }

    [Fact]
    public void ImportBlob_rejects_empty_payload()
    {
        var error = Assert.Throws<ImageImportException>(() => ImageStore.ImportBlob([], "image/png", _doc));
        Assert.Contains("没有图片数据", error.Message);
    }

    [Fact]
    public void ImportFile_keeps_name_then_adds_suffix_on_collision()
    {
        var source = Path.Combine(_dir, "photo.png");
        File.WriteAllBytes(source, Png());

        var first = ImageStore.ImportFile(source, _doc);
        var second = ImageStore.ImportFile(source, _doc);

        Assert.Equal("photo.png", first.FileName);
        Assert.Equal("photo-2.png", second.FileName);
        Assert.Equal(Png(), File.ReadAllBytes(second.Path));
    }

    [Fact]
    public void ImportFile_rejects_non_image_and_missing()
    {
        var text = Path.Combine(_dir, "note.txt");
        File.WriteAllText(text, "not an image");

        Assert.Throws<ImageImportException>(() => ImageStore.ImportFile(text, _doc));
        Assert.Throws<ImageImportException>(() => ImageStore.ImportFile(Path.Combine(_dir, "nope.png"), _doc));
    }

    [Fact]
    public void CleanUnreferenced_deletes_only_unreferenced_images_and_prunes_dirs()
    {
        var keep = ImageStore.ImportBlob(Png(), "image/png", _doc);
        var drop = ImageStore.ImportBlob(Png(), "image/png", _doc);
        var other = Path.Combine(ResourceDir, "notes.txt");
        File.WriteAllText(other, "keep me");

        var deleted = ImageStore.CleanUnreferenced(_doc, [keep.Path]);

        Assert.Equal([drop.FileName], deleted);
        Assert.False(File.Exists(drop.Path));
        Assert.True(File.Exists(keep.Path));
        Assert.True(File.Exists(other));            // 非图片文件不动
        Assert.True(Directory.Exists(ResourceDir)); // 还有内容，目录保留
    }

    [Fact]
    public void CleanUnreferenced_matches_case_insensitively_and_prunes_empty_tree()
    {
        var keep = ImageStore.ImportBlob(Png(), "image/png", _doc);
        Assert.Empty(ImageStore.CleanUnreferenced(_doc, [keep.Path.ToUpperInvariant()]));

        var deleted = ImageStore.CleanUnreferenced(_doc, []);
        Assert.Equal([keep.FileName], deleted);
        Assert.False(Directory.Exists(ResourceDir));
        Assert.False(Directory.Exists(Path.Combine(_dir, "resource")));
    }

    [Fact]
    public void ToResourceUrl_encodes_each_segment()
    {
        var url = ImageStore.ToResourceUrl(@"C:\我的 文档\resource\笔记\img_001.png");
        Assert.StartsWith(AppInfo.ResourcePrefix + "/", url);
        Assert.Contains("C%3A", url);
        Assert.Contains("%E6%88%91%E7%9A%84%20%E6%96%87%E6%A1%A3", url);
        Assert.DoesNotContain('\\', url);
    }

    [Fact]
    public void PathFromText_handles_file_url_and_plain_path()
    {
        var plain = @"C:\a\b.png";
        Assert.Equal(plain, ImageStore.PathFromText("  " + plain + " "));

        var fromUrl = ImageStore.PathFromText("file:///C:/a/b.png");
        Assert.Equal(@"C:\a\b.png", fromUrl.Replace('/', '\\'));
    }
}
