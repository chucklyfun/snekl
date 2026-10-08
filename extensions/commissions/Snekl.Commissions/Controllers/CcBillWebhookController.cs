using Microsoft.AspNetCore.Mvc;
using Snekl.Commissions.Services;

namespace Snekl.Commissions.Controllers;

[ApiController]
public class CcBillWebhookController : ControllerBase
{
    readonly CommissionAppService _app;

    public CcBillWebhookController(CommissionAppService app) => _app = app;

    /// <summary>
    /// CCBill posts form-encoded webhooks to the merchant account URL the artist configures.
    /// Artists paste this path from studio settings into their CCBill admin.
    /// </summary>
    [HttpPost("/api/ccbill/webhook")]
    [Consumes("application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Request.HasFormContentType)
        {
            foreach (var key in Request.Form.Keys)
                form[key] = Request.Form[key].ToString();
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var key = Uri.UnescapeDataString(part[..idx].Replace('+', ' '));
                var value = Uri.UnescapeDataString(part[(idx + 1)..].Replace('+', ' '));
                form[key] = value;
            }
        }

        var (status, message) = await _app.HandleCcBillWebhookAsync(form, ct);
        return StatusCode(status, message);
    }
}
