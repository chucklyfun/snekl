using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Snekl.Domain.Dtos;
using Snekl.Domain.Enums;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserPresentationService _presentation;
    private readonly IPermissionService _permissions;
    private readonly SneklDbContext _db;

    public AuthController(IAuthService auth, IUserPresentationService presentation, IPermissionService permissions, SneklDbContext db)
    {
        _auth = auth;
        _presentation = presentation;
        _permissions = permissions;
        _db = db;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _auth.RegisterAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return result is null ? Unauthorized(new { error = "Invalid credentials." }) : Ok(result);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("reauthenticate")]
    public async Task<ActionResult<AuthResponse>> Reauthenticate(
        [FromBody] ReauthenticateRequest request,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _auth.ReauthenticateAsync(userId, request.Password, ct);
        return result is null ? Unauthorized(new { error = "Invalid password." }) : Ok(result);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("me")]
    public async Task<ActionResult<AuthMeResponse>> Me(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var authSource = User.FindFirstValue("auth_source");

        UserPresentationDto presentation;
        if (authSource == "shadow")
            presentation = await _presentation.GetPresentationAsync(userId, userId, null, ct);
        else
        {
            var user = await _auth.GetUserByIdAsync(userId, ct);
            if (user is null) return NotFound();
            presentation = await _presentation.GetPresentationAsync(userId, userId, null, ct);
        }

        var isSuper = await _permissions.IsSuperUserAsync(userId, ct);
        var isOwner = await _permissions.IsServerOwnerAsync(userId, ct);
        var serverPerms = await _permissions.GetServerPermissionsAsync(userId, ct);
        return Ok(new AuthMeResponse(presentation, isSuper, PermissionNames.ToNames(serverPerms), isOwner));
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet("me/forum-roles")]
    public async Task<ActionResult<IReadOnlyList<MyForumRoleDto>>> MyForumRoles(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var assigned = await _db.ForumRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.Role != null && a.Forum != null)
            .Select(a => new MyForumRoleDto(a.ForumId, a.Forum!.Name, a.Forum.Slug, a.RoleId, a.Role!.Name, "nest"))
            .ToListAsync(ct);
        var memberForums = await _db.ForumMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ForumId)
            .ToListAsync(ct);
        var everyone = await _db.ForumRoles.AsNoTracking()
            .Where(r => r.IsEveryone && memberForums.Contains(r.ForumId) && r.Forum != null)
            .Select(r => new MyForumRoleDto(r.ForumId, r.Forum!.Name, r.Forum.Slug, r.Id, r.Name, "nest"))
            .ToListAsync(ct);
        var seen = new HashSet<Guid>(assigned.Select(role => role.RoleId));
        foreach (var role in everyone)
        {
            if (seen.Add(role.RoleId))
                assigned.Add(role);
        }

        var serverAssigned = await _db.ServerRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.Role != null)
            .Select(a => new MyForumRoleDto(Guid.Empty, "", "", a.RoleId, a.Role!.Name, "server"))
            .ToListAsync(ct);
        var serverEveryone = await _db.ServerRoles.AsNoTracking()
            .Where(r => r.IsEveryone)
            .Select(r => new MyForumRoleDto(Guid.Empty, "", "", r.Id, r.Name, "server"))
            .ToListAsync(ct);
        foreach (var role in serverAssigned.Concat(serverEveryone))
        {
            if (seen.Add(role.RoleId))
                assigned.Add(role);
        }
        return Ok(assigned);
    }
}
