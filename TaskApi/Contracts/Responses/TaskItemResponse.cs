using TaskApi.Models;

namespace TaskApi.Contracts.Responses;

/// <summary>
/// DTO ответа API с данными задачи.
/// </summary>
public sealed class TaskItemResponse
{
    /// <summary>
    /// Идентификатор задачи.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Заголовок задачи.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Признак завершения задачи.
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// Дата и время создания задачи.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Дата и время завершения задачи.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Приоритет задачи.
    /// </summary>
    public Priority Priority { get; init; }

    /// <summary>
    /// Преобразует доменную сущность в DTO ответа API.
    /// </summary>
    public static TaskItemResponse FromEntity(TaskItem taskItem) =>
        new()
        {
            Id = taskItem.Id,
            Title = taskItem.Title,
            IsCompleted = taskItem.IsCompleted,
            CreatedAt = taskItem.CreatedAt,
            CompletedAt = taskItem.CompletedAt,
            Priority = taskItem.Priority
        };
}
