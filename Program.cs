using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SignalRMVC;
using SignalRMVC.Areas.Identity.Data; // Make sure ApplicationUser is here
using SignalRMVC.Models;

var builder = WebApplication.CreateBuilder(args);

// ✅ Use your actual connection string name here
var connectionString = builder.Configuration.GetConnectionString("AppDbContextConnection")
    ?? throw new InvalidOperationException("Connection string 'AppDbContextConnection' not found.");



builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// ✅ Register Identity with your custom ApplicationUser and IdentityRole
//builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
//{
//    options.SignIn.RequireConfirmedAccount = false; // Optional: depends on your need
//})
//.AddEntityFrameworkStores<AppDbContext>()
//.AddDefaultTokenProviders();


builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();


builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // <--- This line fixes the error


builder.Services.AddSignalR();

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
