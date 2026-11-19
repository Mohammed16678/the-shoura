using Microsoft.EntityFrameworkCore;
using Shoura.API.Models;

namespace Shoura.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // تخصيصات إضافية للجداول إن وجدت (مثل تفرد اسم المستخدم Handle)
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Handle)
            .IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.SenderId, m.ReceiverId, m.SentAt });
        modelBuilder.Entity<Post>().HasIndex(p => new { p.CreatedAt, p.Id });
        modelBuilder.Entity<Message>().HasIndex(m => m.ReceiverId);
        modelBuilder.Entity<Message>()
            .HasOne<User>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Message>()
            .HasOne<User>().WithMany().HasForeignKey(m => m.ReceiverId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserBlock>().HasKey(b => new { b.UserId, b.BlockedUserId });
        modelBuilder.Entity<UserBlock>().HasIndex(b => b.BlockedUserId);
        modelBuilder.Entity<UserBlock>().HasOne<User>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserBlock>().HasOne<User>().WithMany().HasForeignKey(b => b.BlockedUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContentReport>().HasIndex(r => new { r.Status, r.CreatedAtUtc });
        modelBuilder.Entity<ContentReport>().HasOne<User>().WithMany().HasForeignKey(r => r.ReporterUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContentReport>().HasOne<User>().WithMany().HasForeignKey(r => r.TargetUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ContentReport>().HasOne<Message>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.SetNull);
    }
}
