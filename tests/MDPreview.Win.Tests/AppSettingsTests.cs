using MDPreview.Win.Settings;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>偏好：缩放夹取、最近列表去重/上限/持久化。</summary>
public sealed class AppSettingsTests
{
    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, 2.5)]
    [InlineData(9.0, 3.0)]
    public void ClampZoom_keeps_scale_in_range(double input, double expected)
        => Assert.Equal(expected, new AppSettings().ClampZoom(input));

    [Fact]
    public void AddRecent_moves_duplicate_to_front_ignoring_case()
    {
        var settings = new AppSettings();
        settings.AddRecent(@"C:\a\one.md");
        settings.AddRecent(@"C:\a\two.md");
        settings.AddRecent(@"C:\A\ONE.md");

        Assert.Equal(2, settings.Recent.Count);
        Assert.Equal(@"C:\A\ONE.md", settings.Recent[0]);
    }

    [Fact]
    public void AddRecent_caps_at_max()
    {
        var settings = new AppSettings();
        for (var i = 0; i < AppSettings.MaxRecent + 5; i++) settings.AddRecent($@"C:\a\{i}.md");

        Assert.Equal(AppSettings.MaxRecent, settings.Recent.Count);
        Assert.Equal($@"C:\a\{AppSettings.MaxRecent + 4}.md", settings.Recent[0]);
    }

    [Fact]
    public void Save_then_load_roundtrips_preferences()
    {
        var settings = new AppSettings
        {
            Theme = "dark",
            Zoom = 1.4,
            MinimapVisible = false,
            CodeLineNumbers = false,
            LastFolder = @"C:\docs",
        };
        settings.AddRecent(@"C:\docs\a.md");

        var loaded = AppSettings.Load();

        Assert.Equal("dark", loaded.Theme);
        Assert.Equal(1.4, loaded.Zoom);
        Assert.False(loaded.MinimapVisible);
        Assert.False(loaded.CodeLineNumbers);
        Assert.Equal(@"C:\docs", loaded.LastFolder);
        Assert.Contains(@"C:\docs\a.md", loaded.Recent);
    }
}
