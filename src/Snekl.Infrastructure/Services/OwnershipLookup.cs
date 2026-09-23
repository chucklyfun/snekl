using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public static class OwnershipLookup
{
    public const string MissingLocalAccountMessage =
        "No account named @{0} on this server yet. They need to sign in here so this node can refresh their login from the auth server.";

    public static string NormalizeUsername(string username)
    {
        var handle = username.Trim().TrimStart('@').ToLowerInvariant();
        if (handle.Length == 0)
            throw new InvalidOperationException("Username is required.");
        return handle;
    }

    public static async Task<User> RequireLocalUserAsync(SneklDbContext db, string username, CancellationToken ct)
    {
        var handle = NormalizeUsername(username);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == handle, ct);
        if (user is null)
            throw new InvalidOperationException(string.Format(MissingLocalAccountMessage, handle));
        return user;
    }
}
