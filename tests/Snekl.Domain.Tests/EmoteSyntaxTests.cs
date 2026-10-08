using Snekl.Domain.Emotes;

namespace Snekl.Domain.Tests;

public class EmoteSyntaxTests
{
    [Theory]
    [InlineData("kappa", "kappa")]
    [InlineData(":Pog:", "pog")]
    [InlineData(" LUL ", "lul")]
    public void TryNormalizeCode_AcceptsSimpleCodes(string raw, string expected)
    {
        Assert.True(EmoteSyntax.TryNormalizeCode(raw, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("Has Space")]
    [InlineData("nope!")]
    public void TryNormalizeCode_RejectsInvalid(string raw)
    {
        Assert.False(EmoteSyntax.TryNormalizeCode(raw, out _));
    }

    [Fact]
    public void IsValidUnicodeEmoji_AcceptsCommonEmoji()
    {
        Assert.True(EmoteSyntax.IsValidUnicodeEmoji("👍"));
        Assert.True(EmoteSyntax.IsValidUnicodeEmoji("❤️"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(":kappa:")]
    [InlineData("")]
    [InlineData("a👍")]
    public void IsValidUnicodeEmoji_RejectsText(string raw)
    {
        Assert.False(EmoteSyntax.IsValidUnicodeEmoji(raw));
    }

    [Fact]
    public void EmojiMatchKey_IgnoresVariationSelector()
    {
        Assert.Equal(EmoteSyntax.EmojiMatchKey("❤"), EmoteSyntax.EmojiMatchKey("❤️"));
    }

    [Fact]
    public void NormalizeQualifier_SlugsNodeNames()
    {
        Assert.Equal("snekl_test_node", EmoteSyntax.NormalizeQualifier("Snekl Test Node"));
    }
}
