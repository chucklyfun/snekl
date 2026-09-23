using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IPostService _posts;

    public SearchController(IPostService posts) => _posts = posts;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchHitDto>>> Get(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _posts.SearchAsync(q, page, pageSize, this.ViewerUserId(), ct));
        }
        catch (Exception ex)
        {
            return ControllerExtensions.HandlePermissionError(ex);
        }
    }
}
