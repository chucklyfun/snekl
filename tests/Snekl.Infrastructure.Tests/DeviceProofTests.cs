using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Snekl.Domain.Security;

namespace Snekl.Infrastructure.Tests;

public class DeviceProofTests
{
    [Fact]
    public void Proof_MatchesThumbprint_AndRejectsReplay()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var x = DeviceProof.Base64Url(parameters.Q.X!);
        var y = DeviceProof.Base64Url(parameters.Q.Y!);
        var thumbprint = DeviceProof.Thumbprint(x, y);
        var token = "access-token";
        var url = "http://localhost/api/me";
        var now = DateTimeOffset.UtcNow;
        var proof = DeviceProof.Create(key, x, y, "GET", url, token, now);
        var replay = new DeviceProofReplayCache();

        Assert.Null(DeviceProof.Validate(proof, thumbprint, "GET", url, token, replay, now));
        Assert.NotNull(DeviceProof.Validate(proof, thumbprint, "GET", url, token, replay, now));
        Assert.NotNull(DeviceProof.Validate(proof, thumbprint, "POST", url, token, new DeviceProofReplayCache(), now));
    }

    [Fact]
    public void BrowserWebCryptoProof_Validates()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "browser-proof.mjs");
        if (!File.Exists(script))
            script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "browser-proof.mjs"));

        var start = new ProcessStartInfo("node", $"\"{script}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var thumbprint = DeviceProof.Thumbprint(root.GetProperty("x").GetString()!, root.GetProperty("y").GetString()!);
        var error = DeviceProof.Validate(
            root.GetProperty("proof").GetString(),
            thumbprint,
            "GET",
            root.GetProperty("url").GetString()!,
            root.GetProperty("token").GetString()!,
            new DeviceProofReplayCache(),
            DateTimeOffset.UtcNow);
        Assert.Null(error);
    }

    [Fact]
    public void PublicJwk_Thumbprint_RejectsPrivateMaterial()
    {
        const string json = """{"kty":"EC","crv":"P-256","x":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","y":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","d":"secret"}""";
        Assert.False(DeviceProof.TryGetThumbprint(json, out _, out var error));
        Assert.Contains("public key", error, StringComparison.Ordinal);
    }
}
