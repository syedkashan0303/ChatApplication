using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Models;

namespace SignalRMVC.Areas.Identity.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<ChatRoom> ChatRoom { get; set; }
    public DbSet<GroupUserMapping> GroupUserMapping { get; set; }
    //public DbSet<AspNetUsers> AspNetUsers { get; set; }
}
