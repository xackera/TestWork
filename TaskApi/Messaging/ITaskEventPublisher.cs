namespace TaskApi.Messaging;

/// <summary>
/// Контракт публикации событий задач во внешний брокер сообщений.
/// </summary>
public interface ITaskEventPublisher
{
    /// <summary>
    /// Публикует событие о завершении задачи во внешний брокер сообщений.
    /// </summary>
    Task PublishTaskCompletedAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken);
}
