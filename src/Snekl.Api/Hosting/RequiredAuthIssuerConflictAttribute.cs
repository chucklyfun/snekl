using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Snekl.Infrastructure.Services;

namespace Snekl.Api.Hosting;

/// <summary>Turns an attempt to drop a server's required auth issuer into 409 with a clear error.</summary>
public sealed class RequiredAuthIssuerConflictAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not RequiredAuthIssuerException required)
            return;

        context.Result = new ConflictObjectResult(new
        {
            error = required.Message,
            code = "required_auth_issuer",
            issuerUrl = required.IssuerUrl
        });
        context.ExceptionHandled = true;
    }
}
