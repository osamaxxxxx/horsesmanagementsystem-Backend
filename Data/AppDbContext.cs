using HorseVillage.Models;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Plot> Plots => Set<Plot>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plot>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_Plots_SerialNo", "[SerialNo] BETWEEN 1 AND 1000"));
            e.HasIndex(p => p.SerialNo).IsUnique();
            e.HasIndex(p => p.ContractNo).IsUnique();
            e.HasIndex(p => p.PlotNo).IsUnique();
            e.HasIndex(p => p.Status);
            e.Property(p => p.BeneficiaryName).HasMaxLength(255);
            e.Property(p => p.ContractNo).HasMaxLength(100);
            e.Property(p => p.StudName).HasMaxLength(255);
            e.Property(p => p.PlotNo).HasMaxLength(50);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(p => p.UpdatedBy).WithMany().HasForeignKey(p => p.UpdatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100);
            e.Property(u => u.FullName).HasMaxLength(255);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(50);
            e.Property(u => u.DeviceInfo).HasMaxLength(255);
            e.Property(u => u.IpAddress).HasMaxLength(50);
        });

        modelBuilder.Entity<UserSession>(e =>
        {
            e.Property(s => s.DeviceInfo).HasMaxLength(255);
            e.Property(s => s.IpAddress).HasMaxLength(50);
            e.HasOne(s => s.User).WithMany(u => u.Sessions).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.PlotId);
            e.HasIndex(a => a.CreatedAt);
            e.Property(a => a.ActionType).HasMaxLength(50);
            // Keep the trail when a plot is deleted; the DELETE entry carries the snapshot.
            e.HasOne<Plot>().WithMany().HasForeignKey(a => a.PlotId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.PerformedBy).WithMany().HasForeignKey(a => a.PerformedById).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
