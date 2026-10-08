using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Snekl.Commissions.Data;

namespace Snekl.Commissions.Services;

public class MemberSessionService
{
    readonly CommissionsDb _db;
    readonly IHttpClientFactory _http;

    public MemberSessionService(CommissionsDb db, IHttpClientFactory http)
    {
        _db = db;
        _http = http;
    }

    public async Task<(MemberSession Session, string Token)> OpenFromParentAsync(
        string contentBaseUrl,
        string meUrl,
        string accessToken,
        string? dpop,
        CancellationToken ct)
    {
        var client = _http.CreateClient("commissions");
        using var req = new HttpRequestMessage(HttpMethod.Get, meUrl);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        if (!string.IsNullOrWhiteSpace(dpop))
            req.Headers.TryAddWithoutValidation("DPoP", dpop);
        using var res = await client.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException("Could not verify member with the content server.");
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var userId = root.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var guid)
            ? guid
            : throw new InvalidOperationException("Member id missing.");
        var username = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
        var display = root.TryGetProperty("displayName", out var d) ? d.GetString() ?? username : username;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new MemberSession
        {
            TokenHash = Hash(token),
            ContentBaseUrl = contentBaseUrl.TrimEnd('/'),
            UserId = userId,
            Username = username,
            DisplayName = display,
            ExpiresAt = DateTime.UtcNow.AddHours(12)
        };
        _db.MemberSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return (session, token);
    }

    public async Task<MemberSession?> ResolveAsync(string? bearer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearer))
            return null;
        var hash = Hash(bearer.Trim());
        var session = await _db.MemberSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || session.ExpiresAt < DateTime.UtcNow)
            return null;
        return session;
    }

    public async Task SignOutAsync(string? bearer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearer))
            return;
        var hash = Hash(bearer.Trim());
        var rows = _db.MemberSessions.Where(s => s.TokenHash == hash);
        _db.MemberSessions.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
