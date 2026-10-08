using Snekl.DevSession;
using Snekl.Faker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FakerOptions>(builder.Configuration.GetSection(FakerOptions.SectionName));
builder.Services.AddHttpClient("faker");
builder.Services.AddSingleton<FakerStore>();
builder.Services.AddSingleton<FakerApiLimits>();
builder.Services.AddSingleton<FakerClient>();
builder.Services.AddSingleton<FakerTokenService>();
builder.Services.AddHostedService<FakerPostingHostedService>();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDevSessionTracking("Snekl.Faker");

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.Run();
