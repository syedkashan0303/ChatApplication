using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SignalRMVC;
using SignalRMVC.Areas.Identity.Data; // Make sure ApplicationUser is here
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using System.Configuration;

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

//builder.Services.AddSignalR();
//builder.Services.AddSingleton<IUserIdProvider, NameUserIdProvider>();


builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // <--- This line fixes the error
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserInfoService>();

builder.Services.AddSingleton<DatabaseJobService>();
builder.Services.AddHostedService<ScheduledTaskService>();

builder.Services.AddSignalR();

//builder.Services.ConfigureApplicationCookie(options =>
//{
//    options.Cookie.Name = "Identity.Cookie";
//    options.Cookie.HttpOnly = true;
//    options.ExpireTimeSpan = TimeSpan.FromSeconds(60); // Optional session timeout
//    options.SlidingExpiration = false;

//    // 🟢 This makes it a session cookie
//    options.Cookie.MaxAge = null;
//});



var app = builder.Build();

// Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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
