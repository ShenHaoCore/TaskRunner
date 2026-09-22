using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Models;

namespace TaskRunner.Core.Persistence;

public sealed class TaskRunnerDbContext(DbContextOptions<TaskRunnerDbContext> options) : DbContext(options)
{
    public DbSet<TaskConfig> TaskConfigs => Set<TaskConfig>();

    public DbSet<SyncWindow> SyncWindows => Set<SyncWindow>();

    public DbSet<RuntimeSetting> RuntimeSettings => Set<RuntimeSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskConfig>();
        task.ToTable("TaskConfigs");
        task.HasKey(item => item.Id);
        task.Property(item => item.Id).ValueGeneratedOnAdd();
        task.HasIndex(item => item.JobId).IsUnique();
        task.Property(item => item.JobId).HasMaxLength(100).IsRequired();
        task.Property(item => item.JobName).HasMaxLength(200).IsRequired();
        task.Property(item => item.CronExpr).HasMaxLength(100).IsRequired();
        task.Property(item => item.JobType).HasMaxLength(500).IsRequired();
        task.Property(item => item.Parameters);
        task.Property(item => item.Description).HasMaxLength(500);
        task.Property(item => item.IsEnabled).HasDefaultValue(true);
        task.Property(item => item.CreatedAt).IsRequired();
        task.Property(item => item.UpdatedAt).IsRequired();

        var window = modelBuilder.Entity<SyncWindow>();
        window.ToTable("SyncWindows");
        window.HasKey(item => item.WindowKey);
        window.Property(item => item.WindowKey).HasMaxLength(32).IsRequired();
        window.Property(item => item.CreatedAt).IsRequired();
        window.HasIndex(item => item.CreatedAt);

        var setting = modelBuilder.Entity<RuntimeSetting>();
        setting.ToTable("RuntimeSettings");
        setting.HasKey(item => item.Key);
        setting.Property(item => item.Key).HasMaxLength(100).IsRequired();
        setting.Property(item => item.Value).HasMaxLength(500).IsRequired();
        setting.Property(item => item.UpdatedAt).IsRequired();
    }
}
