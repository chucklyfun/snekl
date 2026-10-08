using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface INsfwAgeCheckService
{
    Task<NsfwAgeCheckStatusDto> GetStatusAsync(Guid? userId, Guid? forumId, CancellationToken ct = default);
    Task<NsfwAgeCheckStatusDto> SubmitAsync(Guid userId, DateOnly dateOfBirth, Guid? forumId, CancellationToken ct = default);
    Task UpdateServerPolicyAsync(bool required, int? minimumAge, Guid? actorUserId = null, CancellationToken ct = default);
    Task<bool> ViewerMayViewNsfwAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task<bool> ViewerMayEnterForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
    Task EnsureViewerMayEnterForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default);
}

public class NsfwAgeCheckService : INsfwAgeCheckService
{
    private readonly SneklDbContext _db;
    private readonly IChangeLogService? _changeLog;

    public NsfwAgeCheckService(SneklDbContext db, IChangeLogService? changeLog = null)
    {
        _db = db;
        _changeLog = changeLog;
    }

    public async Task<NsfwAgeCheckStatusDto> GetStatusAsync(
        Guid? userId,
        Guid? forumId,
        CancellationToken ct = default)
    {
        var policy = await ResolvePolicyAsync(forumId, ct);
        var attested = await HasAttestedAsync(userId, policy.MinimumAge, ct);
        return ToDto(policy, attested, await ViewerIsBotAsync(userId, ct));
    }

    public async Task<NsfwAgeCheckStatusDto> SubmitAsync(
        Guid userId,
        DateOnly dateOfBirth,
        Guid? forumId,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");
        if (user.IsBot)
            throw new InvalidOperationException("Bots cannot complete an age check.");

        var policy = await ResolvePolicyAsync(forumId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!NsfwAge.MeetsMinimum(dateOfBirth, policy.MinimumAge, today))
            throw new InvalidOperationException(
                $"You must be at least {policy.MinimumAge} years old to view NSFW media.");

        user.NsfwAgeAttestedAt = DateTime.UtcNow;
        user.NsfwAgeAttestedMinimum = policy.MinimumAge;
        await _db.SaveChangesAsync(ct);

        return ToDto(policy, true, false);
    }

    public async Task UpdateServerPolicyAsync(bool required, int? minimumAge, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var node = await _db.NodeConfig.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Node is not configured.");
        var before = new { required = node.NsfwAgeCheckRequired, minimumAge = node.NsfwMinimumAge };
        node.NsfwAgeCheckRequired = required;
        if (minimumAge.HasValue)
            node.NsfwMinimumAge = NsfwAge.ClampMinimumAge(minimumAge.Value);
        else if (node.NsfwMinimumAge < NsfwAge.DefaultMinimumAge)
            node.NsfwMinimumAge = NsfwAge.DefaultMinimumAge;
        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Server,
                null,
                null,
                ChangeLogEntityTypes.NodeConfig,
                node.Id,
                ChangeLogActions.Updated,
                actorUserId,
                "Updated server NSFW age check policy",
                before,
                new { required = node.NsfwAgeCheckRequired, minimumAge = node.NsfwMinimumAge },
                ct);
        }
    }

    public async Task<bool> ViewerMayViewNsfwAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var policy = await ResolvePolicyAsync(forumId, ct);
        return await HasAttestedAsync(userId, policy.MinimumAge, ct);
    }

    public async Task<bool> ViewerMayEnterForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        var policy = await ResolvePolicyAsync(forumId, ct);
        if (!policy.ForumRequired) return true;
        if (await ViewerIsBotAsync(userId, ct)) return true;
        return await HasAttestedAsync(userId, policy.MinimumAge, ct);
    }

    public async Task EnsureViewerMayEnterForumAsync(Guid? userId, Guid forumId, CancellationToken ct = default)
    {
        if (await ViewerMayEnterForumAsync(userId, forumId, ct)) return;
        throw new InvalidOperationException("Confirm your age before entering or joining this nest.");
    }

    private async Task<ResolvedPolicy> ResolvePolicyAsync(Guid? forumId, CancellationToken ct)
    {
        var node = await _db.NodeConfig.AsNoTracking().FirstOrDefaultAsync(ct);
        var serverRequired = node?.NsfwAgeCheckRequired == true;
        var minimumAge = NsfwAge.ClampMinimumAge(node?.NsfwMinimumAge ?? NsfwAge.DefaultMinimumAge);

        if (!forumId.HasValue)
        {
            return new ResolvedPolicy(serverRequired, false, serverRequired, minimumAge, null);
        }

        var forumRequired = false;
        string? explanation = null;
        var currentId = forumId;
        var guard = 0;
        while (currentId.HasValue && guard++ < 32)
        {
            var row = await _db.Forums.AsNoTracking()
                .Where(f => f.Id == currentId.Value)
                .Select(f => new { f.NsfwAgeCheckRequired, f.NsfwAgeCheckExplanation, f.ParentForumId })
                .FirstOrDefaultAsync(ct);
            if (row is null) break;
            if (row.NsfwAgeCheckRequired)
            {
                forumRequired = true;
                if (string.IsNullOrWhiteSpace(explanation) && !string.IsNullOrWhiteSpace(row.NsfwAgeCheckExplanation))
                    explanation = row.NsfwAgeCheckExplanation.Trim();
            }
            currentId = row.ParentForumId;
        }

        return new ResolvedPolicy(
            serverRequired,
            forumRequired,
            serverRequired || forumRequired,
            minimumAge,
            explanation);
    }

    private async Task<bool> HasAttestedAsync(Guid? userId, int minimumAge, CancellationToken ct)
    {
        if (!userId.HasValue) return false;
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.IsBot, u.NsfwAgeAttestedAt, u.NsfwAgeAttestedMinimum })
            .FirstOrDefaultAsync(ct);
        if (user is null || user.IsBot || user.NsfwAgeAttestedAt is null || user.NsfwAgeAttestedMinimum is null)
            return false;
        return user.NsfwAgeAttestedMinimum.Value >= minimumAge;
    }

    private async Task<bool> ViewerIsBotAsync(Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return false;
        return await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId.Value && u.IsBot, ct);
    }

    private static NsfwAgeCheckStatusDto ToDto(ResolvedPolicy policy, bool attested, bool isBot) =>
        new(
            policy.Enforced,
            policy.ServerRequired,
            policy.ForumRequired,
            policy.MinimumAge,
            attested,
            policy.ForumExplanation,
            policy.ForumRequired && !attested && !isBot);

    private readonly record struct ResolvedPolicy(
        bool ServerRequired,
        bool ForumRequired,
        bool Enforced,
        int MinimumAge,
        string? ForumExplanation);
}
