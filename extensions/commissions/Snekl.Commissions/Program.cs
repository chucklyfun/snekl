using Microsoft.EntityFrameworkCore;
using Snekl.Commissions;
using Snekl.Commissions.Data;
using Snekl.Commissions.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CommissionsOptions>(builder.Configuration.GetSection(CommissionsOptions.SectionName));
var options = builder.Configuration.GetSection(CommissionsOptions.SectionName).Get<CommissionsOptions>() ?? new CommissionsOptions();

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var connectionString = options.ConnectionString;
if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
    && !Path.IsPathRooted(connectionString["Data Source=".Length..].Trim()))
{
    var file = connectionString["Data Source=".Length..].Trim();
    connectionString = "Data Source=" + Path.Combine(builder.Environment.ContentRootPath, file);
}

builder.Services.AddDbContext<CommissionsDb>(db => db.UseSqlite(connectionString));
builder.Services.AddHttpClient("commissions");
builder.Services.AddScoped<CommissionAppService>();
builder.Services.AddScoped<MemberSessionService>();
builder.Services.AddControllers();
builder.Services.AddCors(cors =>
    cors.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CommissionsDb>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
