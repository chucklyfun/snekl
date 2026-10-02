using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Infrastructure;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;
using Snekl.DeviceBinding;

using Snekl.Redis;
using Snekl.DevSession;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=snekl_auth;Username=snekl;Password=snekl";

builder.Services.AddAccountInfrastructure(connectionString, builder.Configuration);
builder.Services.AddDeviceProof();

var signingKey = builder.Configuration["Auth:SigningKey"] ?? "snekl-auth-dev-signing-key-change-in-production-min-32";
var authBaseUrl = (builder.Configuration["Auth:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
var adminSigningKey = builder.Configuration["Auth:AdminSigningKey"]
    ?? "snekl-auth-admin-dev-signing-key-change-in-production-min-32";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
    .AddJwtBearer(AuthAdminAuth.Scheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = authBaseUrl,
            ValidAudience = AuthAdminAuth.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(adminSigningKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthAdminAuth.Policy, policy =>
        policy.AddAuthenticationSchemes(AuthAdminAuth.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthAdminAuth.RoleClaim, AuthAdminAuth.Role));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDevSessionTracking("Snekl.Account.Api");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Snekl:ResetDb", false))
    {
        await db.Database.EnsureDeletedAsync();
    }
    await db.Database.EnsureCreatedAsync();
    await db.EnsureOperatorSchemaAsync();

    var server = scope.ServiceProvider.GetRequiredService<IAuthServerService>();
    var config = await server.GetOrCreateAsync();
    var configuredUrl = builder.Configuration["Auth:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredUrl) && config.BaseUrl != configuredUrl.TrimEnd('/'))
    {
        config.BaseUrl = configuredUrl.TrimEnd('/');
        config.Name = builder.Configuration["Auth:Name"] ?? config.Name;
        await db.SaveChangesAsync();
    }

    var oauth = scope.ServiceProvider.GetRequiredService<IOauthService>();
    await oauth.EnsureDevAppsAsync();
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
app.UseSneklRateLimiting(builder.Configuration);
app.UseAuthentication();
app.UseAuthorization();
app.UseDeviceProof();
app.MapControllers();

app.Run();

public partial class Program;
