using Microsoft.AspNetCore.Mvc;
using Snekl.Commissions.Data;
using Snekl.Commissions.Services;

namespace Snekl.Commissions.Controllers;

[ApiController]
[Route("api")]
public class CommissionsController : ControllerBase
{
    readonly CommissionAppService _app;
    readonly MemberSessionService _sessions;

    public CommissionsController(CommissionAppService app, MemberSessionService sessions)
    {
        _app = app;
        _sessions = sessions;
    }

    [HttpPost("session")]
    public async Task<IActionResult> OpenSession([FromBody] ParentSessionRequest request, CancellationToken ct)
    {
        try
        {
            var (session, token) = await _sessions.OpenFromParentAsync(
                request.ContentBaseUrl ?? "",
                request.MeUrl ?? "",
                request.AccessToken ?? "",
                request.Dpop,
                ct);
            return Ok(new { token, userId = session.UserId, username = session.Username, displayName = session.DisplayName });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("session/sign-out")]
    public async Task<IActionResult> SignOut(CancellationToken ct)
    {
        await _sessions.SignOutAsync(Bearer(), ct);
        return Ok();
    }

    [HttpGet("studio")]
    public Task<IActionResult> Studio(CancellationToken ct) =>
        Member(async member => Ok(await _app.GetStudioAsync(member, ct)), ct);

    [HttpPut("studio")]
    public Task<IActionResult> UpsertStudio([FromBody] UpsertStudio request, CancellationToken ct) =>
        Member(async member => Ok(await _app.UpsertStudioAsync(member, request, ct)), ct);

    [HttpGet("listings")]
    public async Task<IActionResult> Listings([FromQuery] string? content, [FromQuery] Guid? artist, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { error = "content is required" });
        return Ok(await _app.ListListingsAsync(content.TrimEnd('/'), artist, ct));
    }

    [HttpGet("artists")]
    public async Task<IActionResult> Artists([FromQuery] string? content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { error = "content is required" });
        var listings = await _app.ListListingsAsync(content.TrimEnd('/'), null, ct);
        var artists = listings
            .GroupBy(l => l.ArtistUserId)
            .Select(g => new { userId = g.Key, username = g.First().ArtistUsername, displayName = g.First().ArtistUsername })
            .ToList();
        return Ok(artists);
    }

    [HttpPost("listings")]
    public Task<IActionResult> CreateListing([FromBody] CreateListing request, CancellationToken ct) =>
        Member(async member => Ok(await _app.CreateListingAsync(member, request, ct)), ct);

    [HttpGet("orders")]
    public Task<IActionResult> Orders(CancellationToken ct) =>
        Member(async member => Ok(await _app.ListOrdersAsync(member, ct)), ct);

    [HttpGet("orders/{id:guid}")]
    public Task<IActionResult> Order(Guid id, CancellationToken ct) =>
        Member(async member => Ok(await _app.GetOrderAsync(member, id, ct)), ct);

    [HttpPost("listings/{id:guid}/requests")]
    public Task<IActionResult> RequestCommission(Guid id, [FromBody] RequestCommission body, CancellationToken ct) =>
        Member(async member => Ok(await _app.RequestAsync(member, id, body, ct)), ct);

    [HttpPost("orders/{id:guid}/quote")]
    public Task<IActionResult> Quote(Guid id, [FromBody] QuoteRequest request, CancellationToken ct) =>
        Member(async member => Ok(await _app.QuoteAsync(member, id, request, ct)), ct);

    [HttpPost("orders/{id:guid}/transition")]
    public Task<IActionResult> Transition(Guid id, [FromBody] TransitionRequest request, CancellationToken ct) =>
        Member(async member => Ok(await _app.TransitionAsync(member, null, id, request.Status ?? "", request.Milestones, ct)), ct);

    [HttpPost("orders/{id:guid}/escrow")]
    public Task<IActionResult> Escrow(Guid id, [FromBody] EscrowRequest request, CancellationToken ct) =>
        Member(async member => Ok(await _app.EscrowAsync(member, null, id, request.State ?? "", ct)), ct);

    [HttpPost("orders/{id:guid}/milestones/{milestoneId:guid}/checkout")]
    public Task<IActionResult> Checkout(Guid id, Guid milestoneId, CancellationToken ct) =>
        Member(async member => Ok(await _app.StartMilestoneCheckoutAsync(member, id, milestoneId, ct)), ct);

    [HttpPost("orders/{id:guid}/notes")]
    public Task<IActionResult> Note(Guid id, [FromBody] NoteRequest request, CancellationToken ct) =>
        Member(async member => Ok(await _app.AddNoteAsync(member, id, request.Body ?? "", ct)), ct);

    async Task<IActionResult> Member(Func<MemberSession, Task<IActionResult>> action, CancellationToken ct)
    {
        try
        {
            var member = await _sessions.ResolveAsync(Bearer(), ct);
            if (member is null)
                return Unauthorized(new { error = "Sign in through Snekl first." });
            return await action(member);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    string? Bearer()
    {
        var header = HttpContext.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        return null;
    }
}

public record ParentSessionRequest(string? ContentBaseUrl, string? MeUrl, string? AccessToken, string? Dpop);
public record EscrowRequest(string? State);
public record NoteRequest(string? Body);
