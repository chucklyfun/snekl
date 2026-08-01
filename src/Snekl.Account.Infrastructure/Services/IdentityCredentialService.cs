using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Account.Domain.Dtos;
using Snekl.Account.Domain.Entities;
using Snekl.Account.Infrastructure.Data;
using Snekl.Domain.Identity.Did;

namespace Snekl.Account.Infrastructure.Services;

public interface IIdentityCredentialService
{
    Task<ExportIdentityResponse> ExportAsync(Guid clusterId, CancellationToken ct = default);
    IdentityCredential? Parse(string json);
    Task<bool> ValidateAsync(IdentityCredential credential, CancellationToken ct = default);
}

public class IdentityCredentialService : IIdentityCredentialService
{
    private readonly AccountDbContext _db;
    private readonly IAuthServerService _server;
    private readonly IHttpClientFactory _httpFactory;

    public IdentityCredentialService(AccountDbContext db, IAuthServerService server, IHttpClientFactory httpFactory)
    {
        _db = db;
        _server = server;
        _httpFactory = httpFactory;
    }

    public async Task<ExportIdentityResponse> ExportAsync(Guid clusterId, CancellationToken ct = default)
    {
        var config = await _server.GetOrCreateAsync(ct);
        var cluster = await _db.PersonClusters.FirstOrDefaultAsync(c => c.Id == clusterId, ct)
            ?? throw new InvalidOperationException("Person cluster not found.");

        var lifetime = TimeSpan.FromMinutes(15);
        var subject = new IdentityCredentialSubject(
            cluster.Did,
            cluster.CanonicalUsername,
            cluster.DisplayName,
            cluster.Email,
            cluster.Bio,
            cluster.DisambiguationTag,
            cluster.PrimaryAuthServerUrl);

        var privateKey = Convert.FromBase64String(config.VerificationPrivateKeyPkcs8);
        var credential = IdentityCredentialBuilder.Create(
            config.Did,
            $"{config.VerificationDid}#{config.VerificationKeyMultibase[1..]}",
            privateKey,
            subject,
            lifetime);

        return new ExportIdentityResponse(IdentityCredentialBuilder.Serialize(credential), DateTime.UtcNow.Add(lifetime));
    }

    public IdentityCredential? Parse(string json) => IdentityCredentialBuilder.Deserialize(json);

    public async Task<bool> ValidateAsync(IdentityCredential credential, CancellationToken ct = default)
    {
        if (!credential.Issuer.StartsWith(DidWeb.Prefix, StringComparison.Ordinal))
            return false;

        var issuerDidDocument = await ResolveDidWebAsync(credential.Issuer, ct);
        if (issuerDidDocument is null) return false;

        var verificationKey = issuerDidDocument.VerificationMethod.FirstOrDefault()?.PublicKeyMultibase;
        if (verificationKey is null) return false;

        return IdentityCredentialBuilder.TryValidate(credential, verificationKey, out _);
    }

    private async Task<DidDocument?> ResolveDidWebAsync(string did, CancellationToken ct)
    {
        if (!did.StartsWith(DidWeb.Prefix, StringComparison.Ordinal)) return null;

        var hostPart = did[DidWeb.Prefix.Length..];
        var host = Uri.UnescapeDataString(hostPart);
        var baseUrl = host.Contains(':') ? $"http://{host}" : $"https://{host}";

        try
        {
            var http = _httpFactory.CreateClient();
            var url = DidWeb.DocumentUrl(baseUrl);
            var doc = await http.GetFromJsonAsync<DidDocument>(url, cancellationToken: ct);
            return doc;
        }
        catch
        {
            return null;
        }
    }
}
