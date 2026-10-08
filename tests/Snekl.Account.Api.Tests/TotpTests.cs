using System.Security.Cryptography;
using System.Text;
using Snekl.Account.Infrastructure.Security;

namespace Snekl.Account.Api.Tests;

public class TotpTests
{
    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    [InlineData(20000000000, "65353130")]
    public void ComputeCode_MatchesRfc6238Sha1Vectors(long unixSeconds, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        var code = Totp.ComputeCode(key, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), digits: 8);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void GenerateSecret_RoundTripsThroughBase32()
    {
        var secret = Totp.GenerateSecret();
        var decoded = Base32.Decode(secret);
        Assert.Equal(20, decoded.Length);
        Assert.Equal(secret, Base32.Encode(decoded));
        Assert.Equal(new byte[] { 0x00 }, Base32.Decode(Base32.Encode(new byte[] { 0x00 })));
    }

    [Fact]
    public void TryMatch_AcceptsCurrentCodeOnce()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = Totp.ComputeCode(secret, now);
        Assert.True(Totp.TryMatch(secret, code, now, null, out var step));
        Assert.False(Totp.TryMatch(secret, code, now, step, out _));

        var wrong = code == "000000" ? "111111" : "000000";
        Assert.False(Totp.TryMatch(secret, wrong, now, null, out _));
    }

    [Fact]
    public void SecretProtector_RoundTripsAndRejectsTampering()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protectedSecret = SecretProtector.Protect("JBSWY3DPEHPK3PXP", key);
        Assert.Equal("JBSWY3DPEHPK3PXP", SecretProtector.Unprotect(protectedSecret, key));

        var bytes = Convert.FromBase64String(protectedSecret);
        bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => SecretProtector.Unprotect(Convert.ToBase64String(bytes), key));
    }
}
