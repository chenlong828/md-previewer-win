using MDPreview.Win.Io;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>语言表：唯一来源是 resources/languages.json，壳只做扩展名映射。</summary>
public sealed class CodeLanguagesTests
{
    [Fact]
    public void ListIsLoadedFromResources()
    {
        Assert.Equal(44, CodeLanguages.All.Count);
        Assert.Contains(CodeLanguages.All, l => l.Id == "csharp");
        Assert.Contains(CodeLanguages.All, l => l.DisplayName == "PowerShell");
    }

    [Theory]
    [InlineData("a.cs", "csharp")]
    [InlineData("a.SWIFT", "swift")]
    [InlineData("Dockerfile", "plaintext")]
    [InlineData("main.tsx", "tsx")]
    [InlineData("styles.scss", "scss")]
    [InlineData("no-extension", "plaintext")]
    public void ExtensionMapsToLanguageId(string name, string expected)
        => Assert.Equal(expected, CodeLanguages.IdForPath(name));

    [Fact]
    public void UnknownExtensionFallsBackToPlainText()
    {
        Assert.Equal(CodeLanguages.PlainText, CodeLanguages.IdForPath("a.zzz"));
        Assert.Equal("C#", CodeLanguages.DisplayName("csharp"));
        Assert.Equal("纯文本", CodeLanguages.DisplayName(CodeLanguages.PlainText));
    }

    [Fact]
    public void ExtensionsAreUniqueAndLowercase()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in CodeLanguages.All)
        {
            Assert.NotEmpty(language.Extensions);
            foreach (var ext in language.Extensions)
            {
                Assert.Equal(ext.ToLowerInvariant(), ext);
                Assert.False(seen.TryGetValue(ext, out var other),
                    $"{ext} 同时属于 {other} 与 {language.Id}");
                seen[ext] = language.Id;
            }
        }
        Assert.True(seen.Count >= 78, $"扩展名数量异常：{seen.Count}");
    }
}
