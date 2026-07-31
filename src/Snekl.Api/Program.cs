using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Snekl.Infrastructure;
using Snekl.Infrastructure.Data;
using Snekl.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl";

builder.Services.AddSneklInfrastructure(connectionString);

var jwtKey = builder.Configuration["Jwt:Key"] ?? "snekl-dev-secret-key-change-in-production-min-32-chars";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "snekl",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "snekl",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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
    var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
    await db.Database.EnsureCreatedAsync();

    var nodeService = scope.ServiceProvider.GetRequiredService<INodeService>();
    var node = await nodeService.GetOrCreateAsync();
    var configuredUrl = builder.Configuration["Node:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredUrl) && node.BaseUrl != configuredUrl)
    {
        await nodeService.UpdateAsync(
            builder.Configuration["Node:Name"] ?? node.Name,
            configuredUrl,
            builder.Configuration["Node:Description"] ?? node.Description);
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
