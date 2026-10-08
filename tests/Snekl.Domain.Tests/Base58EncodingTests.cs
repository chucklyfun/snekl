using System.Text;
using Snekl.Domain.Identity.Did;

namespace Snekl.Domain.Tests;

public class Base58EncodingTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var data = Encoding.UTF8.GetBytes("Snekl base58");
        var encoded = Base58Encoding.Encode(data);
        Assert.Equal(data, Base58Encoding.Decode(encoded));
    }

    [Fact]
    public void Encode_PreservesLeadingZeros()
    {
        var data = new byte[] { 0, 0, 1, 2, 3 };
        var encoded = Base58Encoding.Encode(data);
        Assert.StartsWith("11", encoded);
        Assert.Equal(data, Base58Encoding.Decode(encoded));
    }

    [Fact]
    public void Decode_RejectsInvalidCharacter()
    {
        Assert.Throws<FormatException>(() => Base58Encoding.Decode("0OIl"));
    }
}
