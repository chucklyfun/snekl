using Microsoft.AspNetCore.Mvc;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Controllers;

public static class ControllerExtensions
{
    public static Guid? ViewerUserId(this ControllerBase controller)
    {
        var claim = controller.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim is null ? null : Guid.Parse(claim.Value);
    }

    public static Guid RequireUserId(this ControllerBase controller) =>
        controller.ViewerUserId() ?? throw new UnauthorizedAccessException();

    public static ActionResult HandlePermissionError(Exception ex) =>
        ex switch
        {
            PermissionDeniedException denied => new ObjectResult(new
            {
                error = denied.Message,
                requiredPermission = denied.RequiredPermission,
                scope = denied.Scope
            }) { StatusCode = 403 },
            InvalidOperationException invalid => new BadRequestObjectResult(new { error = invalid.Message }),
            _ => throw ex
        };
}
