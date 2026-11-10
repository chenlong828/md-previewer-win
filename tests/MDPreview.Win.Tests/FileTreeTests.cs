using System.IO;
using MDPreview.Win.Io;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>抽屉取数：只列一层、目录优先、跳过隐藏项、只留可打开类型。</summary>
public sealed class FileTreeTests : IDisposable
{
    readonly string _dir = TestBootstrap.NewCaseDirectory("tree-" + Guid.NewGuid().ToString("N"));

    public FileTreeTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "chapters"));
        Directory.CreateDirectory(Path.Combine(_dir, "node_modules"));
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        File.WriteAllText(Path.Combine(_dir, "b.md"), "# b");
        File.WriteAllText(Path.Combine(_dir, "a.md"), "# a");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "photo.png"), "binary");
        File.WriteAllText(Path.Combine(_dir, ".hidden.md"), "# hidden");
        File.WriteAllText(Path.Combine(_dir, "chapters", "c1.md"), "# c1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Enumerate_lists_directories_first_then_openable_files()
    {
        var entries = FileTree.Enumerate(_dir);

        Assert.Equal(["chapters"], entries.Where(e => e.IsDirectory).Select(e => e.Name));
        Assert.Equal(["a.md", "b.md", "notes.txt"], entries.Where(e => !e.IsDirectory).Select(e => e.Name));
        Assert.DoesNotContain(entries, e => e.Name is "photo.png" or ".hidden.md" or "node_modules" or ".git");
    }

    [Fact]
    public void Enumerate_is_single_level_and_missing_dir_is_empty()
    {
        var chapters = FileTree.Enumerate(Path.Combine(_dir, "chapters"));
        Assert.Equal(["c1.md"], chapters.Select(e => e.Name));

        Assert.Empty(FileTree.Enumerate(Path.Combine(_dir, "nope")));
        Assert.Empty(FileTree.Enumerate(""));
    }

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("a.MARKDOWN", true)]
    [InlineData("main.cs", true)]
    [InlineData("notes.txt", true)]
    [InlineData("photo.png", false)]
    [InlineData("archive.zip", false)]
    public void IsOpenable_matches_text_document_types(string name, bool expected)
        => Assert.Equal(expected, FileTree.IsOpenable(name));

    [Fact]
    public void RootFor_prefers_document_directory_and_falls_back_to_home()
    {
        var doc = Path.Combine(_dir, "a.md");
        Assert.Equal(_dir, FileTree.RootFor(doc));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, FileTree.RootFor(null));
        Assert.Equal(home, FileTree.RootFor(Path.Combine(_dir, "missing", "x.md")));
    }

    [Fact]
    public void IsWithin_accepts_descendants_and_rejects_sibling_prefixes()
    {
        Assert.True(FileTree.IsWithin(_dir, Path.Combine(_dir, "a.md")));
        Assert.True(FileTree.IsWithin(_dir, Path.Combine(_dir, "chapters", "c1.md")));
        Assert.True(FileTree.IsWithin(_dir.ToUpperInvariant(), Path.Combine(_dir, "chapters", "c1.md")));

        var sibling = _dir + "-sibling";
        Directory.CreateDirectory(sibling);
        try
        {
            var outside = Path.Combine(sibling, "x.md");
            File.WriteAllText(outside, "# x");
            Assert.False(FileTree.IsWithin(_dir, outside));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }
}
