using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/lists")]
public class CuratedListsController : ControllerBase
{
    private readonly ICuratedListService _lists;

    public CuratedListsController(ICuratedListService lists) => _lists = lists;

    private Guid? ViewerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.Parse(claim);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CuratedListDto>>> List(CancellationToken ct) =>
        Ok(await _lists.ListAsync(ViewerId(), ct));

    [HttpGet("entries/by-user/{username}")]
    public async Task<ActionResult<IReadOnlyList<UserListEntryHit>>> FindEntriesForUser(
        string username,
        CancellationToken ct) =>
        Ok(await _lists.FindEntriesForUserAsync(username, ViewerId(), ct));

    [HttpGet("{listId:guid}")]
    public async Task<ActionResult<CuratedListDto>> Get(Guid listId, CancellationToken ct)
    {
        var list = await _lists.GetAsync(listId, ViewerId(), ct);
        return list is null ? NotFound() : Ok(list);
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost]
    public async Task<ActionResult<CuratedListDto>> Create(CreateCuratedListRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await _lists.CreateAsync(userId, request, ct);
            return CreatedAtAction(nameof(Get), new { listId = list.Id }, list);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{listId:guid}")]
    public async Task<ActionResult<CuratedListDto>> Update(Guid listId, UpdateCuratedListRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _lists.UpdateAsync(listId, userId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{listId:guid}/transfer")]
    public async Task<ActionResult<CuratedListDto>> Transfer(
        Guid listId,
        TransferCuratedListRequest request,
        CancellationToken ct)
    {
        if (RecentPasswordAuth.RejectIfStale(this) is { } rejected)
            return rejected;

        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _lists.TransferOwnershipAsync(listId, userId, request.Username, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{listId:guid}")]
    public async Task<IActionResult> Delete(Guid listId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _lists.DeleteAsync(listId, userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{listId:guid}/entries")]
    public async Task<ActionResult<PagedResult<ListEntryDto>>> ListEntries(
        Guid listId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _lists.ListEntriesAsync(listId, ViewerId(), page, pageSize, q, ct));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{listId:guid}/entries")]
    public async Task<ActionResult<ListEntryDto>> AddEntry(Guid listId, AddListEntryRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _lists.AddEntryAsync(listId, userId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("{listId:guid}/entries/{entryId:guid}")]
    public async Task<ActionResult<ListEntryDto>> UpdateEntry(Guid listId, Guid entryId, UpdateListEntryRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _lists.UpdateEntryAsync(listId, entryId, userId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{listId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> DeleteEntry(Guid listId, Guid entryId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _lists.DeleteEntryAsync(listId, entryId, userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{listId:guid}/subscribe")]
    public async Task<IActionResult> Subscribe(Guid listId, SubscribeListRequest request, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _lists.SubscribeAsync(listId, userId, request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{listId:guid}/subscribe")]
    public async Task<IActionResult> Unsubscribe(Guid listId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _lists.UnsubscribeAsync(listId, userId, ct);
        return NoContent();
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPut("subscriptions/order")]
    public async Task<IActionResult> Reorder(ReorderListSubscriptionsRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _lists.ReorderSubscriptionsAsync(userId, request.ListIds, ct);
        return NoContent();
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpPost("{listId:guid}/entries/{entryId:guid}/ignore")]
    public async Task<IActionResult> IgnoreEntry(Guid listId, Guid entryId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _lists.IgnoreEntryAsync(listId, entryId, userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpDelete("{listId:guid}/entries/{entryId:guid}/ignore")]
    public async Task<IActionResult> UnignoreEntry(Guid listId, Guid entryId, CancellationToken ct)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _lists.UnignoreEntryAsync(listId, entryId, userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
