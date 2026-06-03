using TaskApi.Models;

namespace TaskApi.Contracts.Requests;

/// <summary>
/// DTO запроса на создание задачи.
/// </summary>
public sealed class CreateTaskRequest
{
    /// <summary>
    /// Заголовок создаваемой задачи.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Приоритет задачи. Если не задан, будет использован Medium.
    /// </summary>
    public Priority? Priority { get; set; }
}
