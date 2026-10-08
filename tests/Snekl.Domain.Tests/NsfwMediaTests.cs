using Snekl.Domain;

namespace Snekl.Domain.Tests;

public class NsfwMediaTests
{
    [Fact]
    public void RequireCover_TrimsAndRejectsBlank()
    {
        Assert.Equal("Hidden photo of a beach.", NsfwMedia.RequireCover("  Hidden photo of a beach.  "));
        Assert.Throws<InvalidOperationException>(() => NsfwMedia.RequireCover("   "));
    }

    [Fact]
    public void NormalizeCover_ClampsLength()
    {
        var longCover = new string('a', NsfwMedia.CoverMaxLength + 20);
        Assert.Equal(NsfwMedia.CoverMaxLength, NsfwMedia.NormalizeCover(longCover).Length);
    }
}
