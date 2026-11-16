using Microsoft.AspNetCore.Identity;
using LinkedInContentAutomationSystem.Models;
using Hangfire;
using LinkedInContentAutomationSystem.BackgroundJobs;
using LinkedInContentAutomationSystem.Data;
using LinkedInContentAutomationSystem.PlaywrightAutomation;
using LinkedInContentAutomationSystem.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Database Context (SQL Server)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<AppDbContext>();

// 3. Register Custom Services
builder.Services.AddScoped<ContentReaderService>();
builder.Services.AddScoped<LinkedInPublisher>();
builder.Services.AddScoped<PublishingJob>();

// 4. Add Hangfire (SQL Server Storage)
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString));

// Add the processing server as IHostedService
builder.Services.AddHangfireServer();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // Required for Identity UI

var app = builder.Build();
// Create the database and apply EF Core migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate(); // Use db.Database.EnsureCreated() if you don't have EF migrations yet
}
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// 5. Use Hangfire Dashboard
app.UseHangfireDashboard("/hangfire");

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages(); // Map Identity routes

// 6. Schedule the recurring job
var postingTimeStr = builder.Configuration["PostingTime"] ?? "10:00";
if (TimeSpan.TryParse(postingTimeStr, out TimeSpan postingTime))
{
    // Configure recurring job (every day at the specified time)
    RecurringJob.AddOrUpdate<PublishingJob>(
        "DailyLinkedInPost",
        job => job.ExecuteAsync(),
        $"{postingTime.Minutes} {postingTime.Hours} * * *" // CRON expression for specific time
    );
}

app.Run();
