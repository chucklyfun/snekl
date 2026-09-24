using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Snekl.Domain.Dtos;
using Snekl.Infrastructure.Security;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

[ApiController]
[Route("api/content-blocks")]
public class ContentBlocksController : ControllerBase
{
    private readonly IContentBlockCatalogService _catalog;

    public ContentBlocksController(IContentBlockCatalogService catalog) => _catalog = catalog;

    [Authorize(Policy = SiteAdminAuthorization.EndUserPolicy)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentBlockTypeDto>>> List(CancellationToken ct) =>
        Ok(await _catalog.ListTypesAsync(ct));
}
