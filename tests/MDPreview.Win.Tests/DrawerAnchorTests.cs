using MDPreview.Win.Shell;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>抽屉锚点：祖先链从盘符根到当前目录，盘符根显示回退为完整路径。</summary>
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
}
