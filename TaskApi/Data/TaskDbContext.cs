using Microsoft.EntityFrameworkCore;
using TaskApi.Models;

namespace TaskApi.Data;

/// <summary>
/// Контекст базы данных приложения.
/// </summary>
public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Набор задач приложения.
    /// </summary>
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>
    /// Настраивает схему сущности TaskItem и ограничения ее полей.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var taskItem = modelBuilder.Entity<TaskItem>();

        taskItem.HasKey(entity => entity.Id);
        taskItem.Property(entity => entity.Title).IsRequired().HasMaxLength(200);
        taskItem.Property(entity => entity.Priority).HasConversion<string>().HasMaxLength(16);
        taskItem.Property(entity => entity.CreatedAt).IsRequired();
        taskItem.Property(entity => entity.RowVersion).IsConcurrencyToken();
    }
}
