using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TaskApi.Contracts.Requests;
using TaskApi.Contracts.Responses;
using TaskApi.Data;
using TaskApi.Messaging;
using TaskApi.Models;

namespace TaskApi.Endpoints;

/// <summary>
/// Регистрирует endpoints для работы с задачами.
/// </summary>
public static class TaskEndpoints
{
    /// <summary>
    /// Добавляет CRUD-маршруты для задач.
    /// </summary>
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/tasks", CreateTaskAsync);
        endpoints.MapGet("/tasks", GetTasksAsync);
        endpoints.MapPut("/tasks/{id:guid}/complete", CompleteTaskAsync);
        endpoints.MapDelete("/tasks/{id:guid}", DeleteTaskAsync);

        return endpoints;
    }

    /// <summary>
    /// Создает новую задачу, валидирует заголовок и заполняет значения по умолчанию.
    /// </summary>
    private static async Task<Results<Created<TaskItemResponse>, ValidationProblem>> CreateTaskAsync(
        CreateTaskRequest request,
        TaskDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateTitle(request.Title);
        if (validationErrors is not null)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var taskItem = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Priority = request.Priority ?? Priority.Medium,
            CreatedAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            CompletedAt = null
        };

        dbContext.Tasks.Add(taskItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/tasks/{taskItem.Id}", TaskItemResponse.FromEntity(taskItem));
    }

    /// <summary>
    /// Возвращает все задачи или часть задач в формате offset/limit.
    /// </summary>
    private static async Task<Results<Ok<PagedResponse<TaskItemResponse>>, ValidationProblem>> GetTasksAsync(
        TaskDbContext dbContext,
        CancellationToken cancellationToken,
        int offset = 0,
        int limit = 0)
    {
        if (offset < 0 || limit < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Параметры offset и limit не могут быть отрицательными."]
            });
        }

        if (limit == 0 && offset > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Параметр offset можно использовать только вместе с положительным limit."]
            });
        }

        var totalCount = await dbContext.Tasks.CountAsync(cancellationToken);

        if (limit == 0)
        {
            var allTasks = await dbContext.Tasks
                .AsNoTracking()
                .OrderBy(task => task.CreatedAt)
                .ThenBy(task => task.Id)
                .Select(task => new TaskItemResponse
                {
                    Id = task.Id,
                    Title = task.Title,
                    IsCompleted = task.IsCompleted,
                    CreatedAt = task.CreatedAt,
                    CompletedAt = task.CompletedAt,
                    Priority = task.Priority
                })
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(new PagedResponse<TaskItemResponse>
            {
                Items = allTasks,
                NextOffset = 0,
                TotalCount = totalCount
            });
        }

        var normalizedLimit = Math.Min(limit, 100);

        var taskPageSeed = await dbContext.Tasks
            .AsNoTracking()
            .Select(task => new
            {
                task.Id,
                task.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var taskIds = taskPageSeed
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .Skip(offset)
            .Take(normalizedLimit)
            .Select(task => task.Id)
            .ToList();

        if (taskIds.Count == 0)
        {
            return TypedResults.Ok(new PagedResponse<TaskItemResponse>
            {
                Items = [],
                NextOffset = 0,
                TotalCount = totalCount
            });
        }

        var tasks = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .ToListAsync(cancellationToken);

        var orderLookup = taskIds
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);

        var orderedTasks = tasks
            .OrderBy(task => orderLookup[task.Id])
            .Select(TaskItemResponse.FromEntity)
            .ToList();

        return TypedResults.Ok(new PagedResponse<TaskItemResponse>
        {
            Items = orderedTasks,
            NextOffset = offset + orderedTasks.Count < totalCount ? offset + orderedTasks.Count : 0,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Завершает задачу, сохраняет время завершения и публикует событие в RabbitMQ.
    /// </summary>
    private static async Task<Results<Ok<TaskItemResponse>, NotFound, Conflict>> CompleteTaskAsync(
        Guid id,
        TaskDbContext dbContext,
        ITaskEventPublisher eventPublisher,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var taskItem = await dbContext.Tasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (taskItem is null)
        {
            return TypedResults.NotFound();
        }

        if (taskItem.IsCompleted)
        {
            return TypedResults.Conflict();
        }

        taskItem.IsCompleted = true;
        taskItem.CompletedAt = DateTimeOffset.UtcNow;
        taskItem.RowVersion++;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict();
        }

        try
        {
            await eventPublisher.PublishTaskCompletedAsync(TaskCompletedEvent.FromTaskItem(taskItem), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RabbitMQ publish failed for task {TaskId}", taskItem.Id);
        }

        return TypedResults.Ok(TaskItemResponse.FromEntity(taskItem));
    }

    /// <summary>
    /// Удаляет задачу без возможности восстановления.
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> DeleteTaskAsync(
        Guid id,
        TaskDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var taskItem = await dbContext.Tasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (taskItem is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Tasks.Remove(taskItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Проверяет, что Title не пустой и укладывается в ограничение по длине.
    /// </summary>
    private static Dictionary<string, string[]>? ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new Dictionary<string, string[]>
            {
                ["title"] = ["Поле Title обязательно."]
            };
        }

        if (title.Trim().Length > 200)
        {
            return new Dictionary<string, string[]>
            {
                ["title"] = ["Поле Title должно быть не длиннее 200 символов."]
            };
        }

        return null;
    }
}
