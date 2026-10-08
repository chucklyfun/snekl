using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public interface IAuthServerService
{
    Task<AuthServerConfig> GetOrCreateAsync(CancellationToken ct = default);
    string SigningKey { get; }
}

public class AuthServerService : IAuthServerService
{
    private readonly AccountDbContext _db;
    private readonly IConfiguration _config;

    public AuthServerService(AccountDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public string SigningKey => _config["Auth:SigningKey"] ?? "snekl-auth-dev-signing-key-change-in-production-min-32";

    public async Task<AuthServerConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        var config = await _db.AuthServerConfig.FirstOrDefaultAsync(ct);
        var created = false;
        if (config is null)
        {
            var baseUrl = (_config["Auth:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
            var verificationKeys = DidKey.Generate();

            config = new AuthServerConfig
            {
                Name = _config["Auth:Name"] ?? "Snekl Auth",
                BaseUrl = baseUrl,
                Did = DidWeb.FromBaseUrl(baseUrl),
                VerificationDid = verificationKeys.Did,
                VerificationKeyMultibase = DidKey.PublicKeyMultibase(verificationKeys.Did),
                VerificationPrivateKeyPkcs8 = Convert.ToBase64String(verificationKeys.PrivateKey),
                SigningKey = SigningKey,
                TotpProtectionKey = NewTotpProtectionKey()
            };
            _db.AuthServerConfig.Add(config);
            created = true;
        }

        var keyAdded = false;
        if (string.IsNullOrWhiteSpace(config.TotpProtectionKey))
        {
            config.TotpProtectionKey = NewTotpProtectionKey();
            keyAdded = true;
        }

        if (created || keyAdded)
            await _db.SaveChangesAsync(ct);

        return config;
    }

    private static string NewTotpProtectionKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
