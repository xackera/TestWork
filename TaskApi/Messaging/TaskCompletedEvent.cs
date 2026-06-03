using TaskApi.Models;

namespace TaskApi.Messaging;

/// <summary>
/// Событие завершения задачи для публикации в RabbitMQ.
/// </summary>
public sealed class TaskCompletedEvent
{
    /// <summary>
    /// Идентификатор завершенной задачи.
    /// </summary>
    public Guid TaskId { get; init; }

    /// <summary>
    /// Заголовок завершенной задачи.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Время завершения задачи.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Приоритет завершенной задачи.
    /// </summary>
    public string Priority { get; init; } = string.Empty;

    /// <summary>
    /// Формирует payload события из завершенной задачи.
    /// </summary>
    public static TaskCompletedEvent FromTaskItem(TaskItem taskItem) =>
        new()
        {
            TaskId = taskItem.Id,
            Title = taskItem.Title,
            CompletedAt = taskItem.CompletedAt ?? DateTimeOffset.UtcNow,
            Priority = taskItem.Priority.ToString()
        };
}
