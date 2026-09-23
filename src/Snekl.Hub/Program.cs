using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Snekl.DevSession;
using Snekl.Hub;
using Snekl.Hub.Infrastructure;
using Snekl.Hub.Infrastructure.Data;
using Snekl.Hub.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var hubConnection = builder.Configuration.GetConnectionString("Hub")
    ?? "Host=localhost;Port=5432;Database=snekl_hub;Username=snekl;Password=snekl";

builder.Services.AddHubInfrastructure(hubConnection, builder.Configuration);
builder.Services.AddHostedService<HubFeedOriginSyncHostedService>();

var authBaseUrl = (builder.Configuration["Hub:AuthBaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
var signingKey = builder.Configuration["Hub:AuthSigningKey"]
    ?? builder.Configuration["Auth:SigningKey"]
    ?? "snekl-auth-dev-signing-key-change-in-production-min-32";
var hubBaseUrl = (builder.Configuration["Hub:BaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
var adminSigningKey = builder.Configuration["Hub:AdminSigningKey"]
    ?? "snekl-hub-admin-dev-signing-key-change-in-production-min-32";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = authBaseUrl,
            ValidAudience = "snekl",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    })
    .AddJwtBearer(HubAdminAuth.Scheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = hubBaseUrl,
            ValidAudience = HubAdminAuth.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(adminSigningKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(HubAdminAuth.Policy, policy =>
        policy.AddAuthenticationSchemes(HubAdminAuth.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(HubAdminAuth.RoleClaim, HubAdminAuth.Role));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDevSessionTracking("Snekl.Hub");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Snekl:ResetDb", false))
        await db.Database.EnsureDeletedAsync();

    await db.Database.EnsureCreatedAsync();
    await db.EnsureMembershipRequestColumnsAsync();
    await db.EnsureFeedCardSchemaAsync();
    await db.EnsureForumMembershipSchemaAsync();

    if (!await db.FederationState.AnyAsync())
    {
        db.FederationState.Add(new Snekl.Hub.Domain.Entities.FederationState());
        await db.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSneklForwardedHeaders();
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/admin" || context.Request.Path == "/admin/")
        context.Request.Path = "/admin/index.html";
    await next();
});
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Optional full member SPA only when wwwroot/index.html is present.
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
if (File.Exists(spaIndex))
    app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
