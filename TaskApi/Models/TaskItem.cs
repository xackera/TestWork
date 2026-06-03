namespace TaskApi.Models;

/// <summary>
/// Представляет задачу в системе.
/// </summary>
public sealed class TaskItem
{
    /// <summary>
    /// Уникальный идентификатор задачи.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Заголовок задачи.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Признак того, что задача завершена.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Момент создания задачи.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Момент завершения задачи.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Приоритет задачи.
    /// </summary>
    public Priority Priority { get; set; }

    /// <summary>
    /// Версия строки для optimistic concurrency.
    /// </summary>
    public long RowVersion { get; set; }
}
