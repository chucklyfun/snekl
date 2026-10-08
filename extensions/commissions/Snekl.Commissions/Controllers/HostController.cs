using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Snekl.Commissions.Controllers;

[ApiController]
public class HostController : ControllerBase
{
    readonly CommissionsOptions _options;

    public HostController(IOptions<CommissionsOptions> options) => _options = options.Value;

    [HttpGet("/.well-known/snekl-extension.json")]
    public IActionResult Manifest() => Ok(new
    {
        id = _options.ClientId,
        name = "Snekl Commissions",
        version = "0.2.0",
        adminUrl = _options.PublicBaseUrl.TrimEnd('/') + "/",
        iframeUrl = _options.PublicBaseUrl.TrimEnd('/') + "/",
        description = "Artist studios, commission requests, escrow agreement records, and adult CCBill milestones."
    });

    [HttpGet("/api/host/status")]
    public IActionResult Status() => Ok(new
    {
        publicBaseUrl = _options.PublicBaseUrl,
        ccBillWebhookUrl = _options.WebhookUrl,
        note = "Adult CCBill milestones settle to the artist's merchant account. Do not charge adult orders on the Snekl Stripe account."
    });
}
