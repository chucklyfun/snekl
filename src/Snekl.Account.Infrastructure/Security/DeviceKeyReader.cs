using System.Text.Json;
using Snekl.Account.Domain.Dtos;
using Snekl.Domain.Security;

namespace Snekl.Account.Infrastructure.Security;

public static class DeviceKeyReader
{
    public static bool TryRead(DevicePublicJwk? bodyKey, string? headerJson, out string thumbprint, out string error)
    {
        if (bodyKey is not null)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["kty"] = bodyKey.Kty,
                ["crv"] = bodyKey.Crv,
                ["x"] = bodyKey.X,
                ["y"] = bodyKey.Y
            });
            return DeviceProof.TryGetThumbprint(json, out thumbprint, out error);
        }

        if (!string.IsNullOrWhiteSpace(headerJson))
            return DeviceProof.TryGetThumbprint(headerJson, out thumbprint, out error);

        thumbprint = "";
        error = "A device key from this browser is required.";
        return false;
    }
}
