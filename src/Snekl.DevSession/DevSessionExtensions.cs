using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Snekl.DevSession;

public sealed class DevSessionMiddleware(RequestDelegate next, DevSessionStore store, int pid)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        try
        {
            var client = context.Request.Headers["X-Snekl-Dev-Client"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(client))
                client = "unknown";

            store.RecordRequest(pid, context.Request.Method, context.Request.Path.Value ?? "/", client);
        }
        catch
        {
            // Dev session tracking must never fail a request.
        }
    }
}

public static class DevSessionExtensions
{
    public static WebApplication UseDevSessionTracking(this WebApplication app, string serviceName)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        var repoRoot = RepoPaths.FindRoot(Directory.GetCurrentDirectory());
        var store = new DevSessionStore(repoRoot);
        var pid = Environment.ProcessId;
        var urls = app.Configuration["DOTNET_URLS"] ?? app.Configuration["ASPNETCORE_URLS"];
        var errorLog = RepoPaths.ErrorLogFile(repoRoot);
        ErrorFileLog.Note(errorLog, serviceName, pid);
        if (app.Services.GetService<ILoggerFactory>() is LoggerFactory factory)
            factory.AddProvider(new ErrorFileLoggerProvider(errorLog, serviceName));

        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex) when (IsClientAbort(context, ex))
            {
                // The browser moved on. That is not a server failure.
            }
            catch (Exception ex)
            {
                ErrorFileLog.Write(
                    errorLog,
                    serviceName,
                    $"{context.Request.Method} {context.Request.GetEncodedPathAndQuery()}",
                    ex);
                throw;
            }
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                store.Register(pid, serviceName, urls);
            }
            catch
            {
                // Tracking is best-effort; never fail host startup.
            }
        });
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                store.Unregister(pid);
            }
            catch
            {
                // Best-effort cleanup.
            }
        });
        app.UseMiddleware<DevSessionMiddleware>(store, pid);

        return app;
    }

    private static bool IsClientAbort(HttpContext context, Exception exception)
    {
        if (!context.RequestAborted.IsCancellationRequested)
            return false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or TaskCanceledException)
                return true;
        }

        return false;
    }
}
