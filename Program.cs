using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SignalRMVC;
using SignalRMVC.Areas.Identity.Data; // Make sure ApplicationUser is here
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using Serilog.Events;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// ✅ Use your actual connection string name here
var connectionString = builder.Configuration.GetConnectionString("AppDbContextConnection")
    ?? throw new InvalidOperationException("Connection string 'AppDbContextConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequiredUniqueChars = 0;
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // <--- This line fixes the error
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserInfoService>();

builder.Services.AddSingleton<DatabaseJobService>();
builder.Services.AddHostedService<ScheduledTaskService>();
builder.Services.AddHttpClient(); // ✅ Register IHttpClientFactory
//builder.Services.AddHostedService<AppWatchdogService>();

//builder.Services.AddSignalR();
builder.Services.AddSignalR(options =>
{
    options.AddFilter<LoggingHubFilter>();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});


// Serilog setup moved to Host.UseSerilog
// Log.Logger setup removed

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"D:\ChatAppKeys"))
    .SetApplicationName("arynewschat");


builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();


app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        if (exception is AntiforgeryValidationException antiEx)
        {
            logger.LogError(antiEx, "Antiforgery token validation failed on path: {Path}", context.Request.Path);
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid or expired CSRF token.");
            return;
        }

        logger.LogError(exception, "Unhandled exception occurred on path: {Path}", context.Request.Path);
        await context.Response.WriteAsync("An unexpected error occurred.");
    });
});

app.UseMiddleware<GlobalExceptionMiddleware>(); // 👈 Add this first
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSerilogRequestLogging();
//app.UseMiddleware<ResponseTimeMiddleware>();

app.UseRouting();

app.UseAuthentication(); // ✅ Important for Identity
app.UseAuthorization();

// Routing
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // ✅ For Identity UI pages
app.MapHub<BasicChatHub>("/hubs/basicchat"); // ✅ SignalR Hub route

app.Run();
