namespace TaskApi.Contracts.Responses;

/// <summary>
/// Универсальный ответ для получения списка задач.
/// </summary>
public sealed class PagedResponse<TItem>
{
    /// <summary>
    /// Элементы текущей выборки.
    /// </summary>
    public IReadOnlyList<TItem> Items { get; init; } = [];

    /// <summary>
    /// Смещение для следующего запроса. Если данных больше нет, возвращается null.
    /// </summary>
    public int NextOffset { get; init; }

    /// <summary>
    /// Общее количество элементов.
    /// </summary>
    public int TotalCount { get; init; }
}
