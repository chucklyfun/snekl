using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Snekl.Account.Infrastructure;
using Snekl.Account.Infrastructure.Data;
using Snekl.Account.Infrastructure.Security;
using Snekl.Account.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=snekl_auth;Username=snekl;Password=snekl";

builder.Services.AddAccountInfrastructure(connectionString);

var signingKey = builder.Configuration["Auth:SigningKey"] ?? "snekl-auth-dev-signing-key-change-in-production-min-32";
var authBaseUrl = (builder.Configuration["Auth:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');

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
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
    if (app.Environment.IsDevelopment())
    {
        await db.Database.EnsureDeletedAsync();
    }
    await db.Database.EnsureCreatedAsync();

    var server = scope.ServiceProvider.GetRequiredService<IAuthServerService>();
    var config = await server.GetOrCreateAsync();
    var configuredUrl = builder.Configuration["Auth:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredUrl) && config.BaseUrl != configuredUrl.TrimEnd('/'))
    {
        config.BaseUrl = configuredUrl.TrimEnd('/');
        config.Name = builder.Configuration["Auth:Name"] ?? config.Name;
        await db.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
