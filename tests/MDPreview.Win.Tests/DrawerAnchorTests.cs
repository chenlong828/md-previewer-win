using MDPreview.Win.Shell;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>抽屉锚点：祖先链从盘符根到当前目录，盘符根显示回退为完整路径；打开后按来源决定锚点。</summary>
public sealed class DrawerAnchorTests
{
    [Fact]
    public void Chain_lists_drive_root_to_current_directory()
    {
        var chain = DrawerAnchor.Chain(@"C:\work\docs");

        Assert.Equal(new[] { @"C:\", @"C:\work", @"C:\work\docs" }, chain);
    }

    [Fact]
    public void Display_name_of_drive_root_falls_back_to_full_path()
    {
        Assert.Equal(@"C:\", DrawerAnchor.DisplayName(@"C:\"));
        Assert.Equal("docs", DrawerAnchor.DisplayName(@"C:\work\docs"));
    }

    [Fact]
    public void Root_after_external_open_reanchors_to_document_directory()
    {
        Assert.Equal(@"C:\work\docs", DrawerAnchor.RootAfterOpen(@"C:\work", @"C:\work\docs\a.md", reanchor: true));
    }

    [Fact]
    public void Root_after_drawer_open_keeps_anchor_for_descendant_files()
    {
        Assert.Equal(@"C:\work", DrawerAnchor.RootAfterOpen(@"C:\work", @"C:\work\docs\a.md", reanchor: false));
        Assert.Equal(@"C:\work", DrawerAnchor.RootAfterOpen(@"C:\work", @"C:\work\b.md", reanchor: false));
    }

    [Fact]
    public void Root_after_drawer_open_reanchors_when_file_leaves_the_anchor()
    {
        Assert.Equal(@"D:\other", DrawerAnchor.RootAfterOpen(@"C:\work", @"D:\other\a.md", reanchor: false));
        Assert.Equal(@"C:\work\docs", DrawerAnchor.RootAfterOpen(null, @"C:\work\docs\a.md", reanchor: false));
    }
}
