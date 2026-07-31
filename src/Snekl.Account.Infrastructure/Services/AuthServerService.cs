using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;

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
        if (config is not null) return config;

        config = new AuthServerConfig
        {
            Name = _config["Auth:Name"] ?? "Snekl Auth",
            BaseUrl = (_config["Auth:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/'),
            SigningKey = SigningKey
        };
        _db.AuthServerConfig.Add(config);
        await _db.SaveChangesAsync(ct);
        return config;
    }
}
