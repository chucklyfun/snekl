using Microsoft.EntityFrameworkCore;
using Snekl.Domain;
using Snekl.Domain.Dtos;
using Snekl.Domain.Entities;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Services;

public interface IForumModerationService
{
    Task<bool> IsBannedAsync(Guid forumId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumBanDto>> ListBansAsync(Guid forumId, CancellationToken ct = default);
    Task BanUserAsync(Guid forumId, Guid moderatorUserId, string username, string? reason, DateTime? expiresAt, CancellationToken ct = default);
    Task UnbanUserAsync(Guid forumId, Guid userId, Guid? actorUserId = null, CancellationToken ct = default);
    Task<PostReportDto> ReportPostAsync(Guid reporterUserId, Guid postId, string reason, CancellationToken ct = default);
    Task<IReadOnlyList<PostReportDto>> ListOpenReportsAsync(Guid forumId, CancellationToken ct = default);
    Task DismissReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default);
    Task ResolveReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default);
}

public class ForumModerationService : IForumModerationService
{
    private readonly SneklDbContext _db;
    private readonly IChangeLogService? _changeLog;

    public ForumModerationService(SneklDbContext db, IChangeLogService? changeLog = null)
    {
        _db = db;
        _changeLog = changeLog;
    }

    public async Task<bool> IsBannedAsync(Guid forumId, Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.ForumMemberBans.AnyAsync(
            b => b.ForumId == forumId && b.UserId == userId && (b.ExpiresAt == null || b.ExpiresAt > now),
            ct);
    }

    public async Task<IReadOnlyList<ForumBanDto>> ListBansAsync(Guid forumId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var bans = await _db.ForumMemberBans
            .Include(b => b.User)
            .Include(b => b.BannedBy)
            .Where(b => b.ForumId == forumId && (b.ExpiresAt == null || b.ExpiresAt > now))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        return bans.Select(b => new ForumBanDto(
            b.Id,
            b.UserId,
            b.User!.Username,
            b.User.DisplayName,
            b.Reason,
            b.ExpiresAt,
            b.BannedBy!.Username,
            b.CreatedAt)).ToList();
    }

    public async Task BanUserAsync(
        Guid forumId,
        Guid moderatorUserId,
        string username,
        string? reason,
        DateTime? expiresAt,
        CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct)
            ?? throw new InvalidOperationException("User not found.");

        var existing = await _db.ForumMemberBans
            .FirstOrDefaultAsync(b => b.ForumId == forumId && b.UserId == user.Id, ct);

        if (existing is null)
        {
            _db.ForumMemberBans.Add(new ForumMemberBan
            {
                ForumId = forumId,
                UserId = user.Id,
                BannedByUserId = moderatorUserId,
                Reason = reason?.Trim(),
                ExpiresAt = expiresAt
            });
        }
        else
        {
            existing.Reason = reason?.Trim();
            existing.ExpiresAt = expiresAt;
            existing.BannedByUserId = moderatorUserId;
            existing.CreatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Forum,
                forumId,
                null,
                ChangeLogEntityTypes.ForumBan,
                user.Id,
                ChangeLogActions.Updated,
                moderatorUserId,
                $"Banned @{user.Username}",
                null,
                new { username = user.Username, reason = reason?.Trim(), expiresAt },
                ct);
        }
    }

    public async Task UnbanUserAsync(Guid forumId, Guid userId, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var ban = await _db.ForumMemberBans
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.ForumId == forumId && b.UserId == userId, ct);
        if (ban is null) return;

        var username = ban.User?.Username ?? userId.ToString();
        _db.ForumMemberBans.Remove(ban);
        await _db.SaveChangesAsync(ct);
        if (_changeLog is not null)
        {
            await _changeLog.RecordAsync(
                ChangeLogScopes.Forum,
                forumId,
                null,
                ChangeLogEntityTypes.ForumBan,
                userId,
                ChangeLogActions.Deleted,
                actorUserId,
                $"Unbanned @{username}",
                new { username, reason = ban.Reason, expiresAt = ban.ExpiresAt },
                null,
                ct);
        }
    }

    public async Task<PostReportDto> ReportPostAsync(Guid reporterUserId, Guid postId, string reason, CancellationToken ct = default)
    {
        var trimmed = reason.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Reason is required.");

        var post = await _db.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new InvalidOperationException("Post not found.");

        if (post.AuthorUserId == reporterUserId)
            throw new InvalidOperationException("You cannot report your own post.");

        var hasOpen = await _db.PostReports.AnyAsync(
            r => r.PostId == postId && r.ReporterUserId == reporterUserId && r.Status == PostReportStatus.Open,
            ct);
        if (hasOpen)
            throw new InvalidOperationException("You already have an open report on this post.");

        var reporter = await _db.Users.FindAsync([reporterUserId], ct);
        Guid forumId;
        if (post.ForumId.HasValue)
            forumId = post.ForumId.Value;
        else if (post.WatercoolerId.HasValue)
        {
            var cooler = await _db.Watercoolers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == post.WatercoolerId.Value, ct)
                ?? throw new InvalidOperationException("Watercooler not found.");
            forumId = cooler.ParentForumId
                ?? throw new InvalidOperationException("Server watercoolers do not have a forum report queue yet.");
        }
        else
            throw new InvalidOperationException("Post not found.");

        var report = new PostReport
        {
            PostId = postId,
            ForumId = forumId,
            ReporterUserId = reporterUserId,
            Reason = trimmed
        };
        _db.PostReports.Add(report);
        await _db.SaveChangesAsync(ct);

        return ToReportDto(report, post, reporter!);
    }

    public async Task<IReadOnlyList<PostReportDto>> ListOpenReportsAsync(Guid forumId, CancellationToken ct = default)
    {
        var reports = await _db.PostReports
            .Include(r => r.Post).ThenInclude(p => p!.Author)
            .Include(r => r.Reporter)
            .Where(r => r.ForumId == forumId && r.Status == PostReportStatus.Open)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return reports.Select(r => ToReportDto(r, r.Post!, r.Reporter!)).ToList();
    }

    public async Task DismissReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default)
    {
        var report = await _db.PostReports.FindAsync([reportId], ct)
            ?? throw new InvalidOperationException("Report not found.");

        report.Status = PostReportStatus.Dismissed;
        report.ResolvedByUserId = moderatorUserId;
        report.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await RecordReportAsync(report, moderatorUserId, "Dismissed post report", ct);
    }

    public async Task ResolveReportAsync(Guid moderatorUserId, Guid reportId, CancellationToken ct = default)
    {
        var report = await _db.PostReports.FindAsync([reportId], ct)
            ?? throw new InvalidOperationException("Report not found.");

        report.Status = PostReportStatus.Resolved;
        report.ResolvedByUserId = moderatorUserId;
        report.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await RecordReportAsync(report, moderatorUserId, "Resolved post report", ct);
    }

    private async Task RecordReportAsync(PostReport report, Guid actorUserId, string summary, CancellationToken ct)
    {
        if (_changeLog is null) return;
        await _changeLog.RecordAsync(
            ChangeLogScopes.Forum,
            report.ForumId,
            null,
            ChangeLogEntityTypes.PostReport,
            report.Id,
            ChangeLogActions.Updated,
            actorUserId,
            summary,
            null,
            new { report.Status, report.PostId },
            ct);
    }

    private static PostReportDto ToReportDto(PostReport report, Post post, User reporter)
    {
        var preview = post.IsDeleted ? "[deleted]" : post.Message;
        if (preview.Length > 120) preview = preview[..120] + "…";

        return new PostReportDto(
            report.Id,
            report.PostId,
            preview,
            post.Author?.Username ?? "unknown",
            report.ReporterUserId,
            reporter.Username,
            report.Reason,
            report.Status,
            report.CreatedAt,
            report.ResolvedAt);
    }
}
