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

        // Configure UserLoginLogs table
        builder.Entity<UserLoginLog>(entity =>
        {
            entity.ToTable("UserLoginLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(450).IsRequired();
            entity.Property(e => e.LoginDateTime).HasColumnName("LoginDateTime").IsRequired();

            // Index for fast search by UserId and LoginDateTime
            entity.HasIndex(e => new { e.UserId, e.LoginDateTime })
                .HasDatabaseName("IX_UserLoginLogs_UserId_LoginDateTime");
        });
    }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<UsersMessage> UsersMessage { get; set; }
    public DbSet<ChatRoom> ChatRoom { get; set; }
    public DbSet<GroupUserMapping> GroupUserMapping { get; set; }
    public DbSet<EditedMessagesLog> EditedtMessagesLogs { get; set; }
    public DbSet<ChatLog> ChatLogs { get; set; }
    public DbSet<ChatMessageReadStatus> ChatMessageReadStatuses { get; set; }
    public DbSet<UsersMessageReadStatus> UsersMessageReadStatus { get; set; }
    public DbSet<UserLoginLog> UserLoginLogs { get; set; }

    //public DbSet<UserRole> AspNetRoles { get; set; }
    //public DbSet<AspNetUsers> AspNetUsers { get; set; }
}
