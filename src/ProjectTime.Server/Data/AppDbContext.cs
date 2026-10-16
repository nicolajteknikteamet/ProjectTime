using Microsoft.EntityFrameworkCore;

namespace ProjectTime.Server.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Device>().HasIndex(d => d.ApiKeyHash).IsUnique();
        b.Entity<Device>().HasIndex(d => d.Name).IsUnique();

        b.Entity<Activity>().HasIndex(a => new { a.DeviceId, a.ClientId }).IsUnique();
        b.Entity<Activity>().HasIndex(a => a.StartUtc);
        b.Entity<Activity>().HasIndex(a => a.EndUtc);
        b.Entity<Activity>().HasIndex(a => a.ProjectId);
        b.Entity<Activity>().HasOne(a => a.Device).WithMany(d => d.Activities).HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Activity>().HasOne(a => a.Project).WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Rule>().HasOne(r => r.Project).WithMany(p => p.Rules).HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<TimeEntry>().HasIndex(t => t.StartUtc);
        b.Entity<TimeEntry>().HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
