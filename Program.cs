using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignalRMVC;
using SignalRMVC.Areas.Identity.Data; // Make sure ApplicationUser is here
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using Serilog;
using Microsoft.AspNetCore.DataProtection;

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

builder.Services.AddSignalR();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug() // capture everything
    .WriteTo.File(
        @"D:\ChatAppLogs\ChatApp-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.File(
        @"D:\ChatAppLogs\ChatApp-Errors-.log",
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"D:\ChatAppKeys"))
    .SetApplicationName("arynewschat");


builder.Host.UseSerilog(); // Plug Serilog into ASP.NET Core

var app = builder.Build();

// Middleware
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Home/Error");
//    app.UseHsts();
//}

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


app.UseHttpsRedirection();
app.UseStaticFiles();
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
