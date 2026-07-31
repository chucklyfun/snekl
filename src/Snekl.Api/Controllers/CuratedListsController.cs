using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
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

    [HttpGet("{listId:guid}")]
    public async Task<ActionResult<CuratedListDto>> Get(Guid listId, CancellationToken ct)
    {
        var list = await _lists.GetAsync(listId, ViewerId(), ct);
        return list is null ? NotFound() : Ok(list);
    }

    [Authorize]
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

    [Authorize]
    [HttpPost("{listId:guid}/entries")]
    public async Task<ActionResult<ListAnnotationDto>> AddEntry(Guid listId, AddListEntryRequest request, CancellationToken ct)
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

    [Authorize]
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

    [Authorize]
    [HttpDelete("{listId:guid}/subscribe")]
    public async Task<IActionResult> Unsubscribe(Guid listId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _lists.UnsubscribeAsync(listId, userId, ct);
        return NoContent();
    }
}
