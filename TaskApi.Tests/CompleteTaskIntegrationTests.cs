using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaskApi.Contracts.Requests;
using TaskApi.Contracts.Responses;
using TaskApi.Data;
using TaskApi.Messaging;
using TaskApi.Models;

namespace TaskApi.Tests;

public sealed class CompleteTaskIntegrationTests : IClassFixture<TaskApiFactory>
{
    private readonly TaskApiFactory _factory;

    public CompleteTaskIntegrationTests(TaskApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteTask_PublishesEvent()
    {
        await ResetStateAsync();

        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
        {
            Title = "Buy milk",
            Priority = Priority.High
        });

        createResponse.EnsureSuccessStatusCode();

        var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskItemResponse>();
        Assert.NotNull(createdTask);

        var completeResponse = await client.PutAsync($"/tasks/{createdTask!.Id}/complete", content: null);
        completeResponse.EnsureSuccessStatusCode();

        var completedTask = await completeResponse.Content.ReadFromJsonAsync<TaskItemResponse>();

        Assert.NotNull(completedTask);
        Assert.True(completedTask!.IsCompleted);
        Assert.NotNull(completedTask.CompletedAt);

        var publishedEvent = Assert.Single(_factory.Publisher.Events);
        Assert.Equal(createdTask.Id, publishedEvent.TaskId);
        Assert.Equal("Buy milk", publishedEvent.Title);
        Assert.Equal("High", publishedEvent.Priority);
        Assert.Equal(completedTask.CompletedAt, publishedEvent.CompletedAt);
    }

    [Fact]
    public async Task CreateTask_WithWhitespaceTitle_ReturnsBadRequest()
    {
        await ResetStateAsync();

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
        {
            Title = "   "
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_WithoutPagination_ReturnsAllTasksInPagedResponse()
    {
        await ResetStateAsync();

        var client = _factory.CreateClient();

        for (var index = 1; index <= 3; index++)
        {
            var createResponse = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
            {
                Title = $"Task {index}"
            });

            createResponse.EnsureSuccessStatusCode();
        }

        var response = await client.GetFromJsonAsync<PagedResponse<TaskItemResponse>>("/tasks");

        Assert.NotNull(response);
        Assert.Equal(0, response!.NextOffset);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(["Task 1", "Task 2", "Task 3"], response.Items.Select(task => task.Title).ToArray());
    }

    [Fact]
    public async Task GetTasks_WithOffsetAndLimit_ReturnsPagedResponse()
    {
        await ResetStateAsync();

        var client = _factory.CreateClient();

        for (var index = 1; index <= 3; index++)
        {
            var response = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
            {
                Title = $"Task {index}"
            });

            response.EnsureSuccessStatusCode();
        }

        var pageResponse = await client.GetFromJsonAsync<PagedResponse<TaskItemResponse>>("/tasks?offset=1&limit=1");

        Assert.NotNull(pageResponse);
        Assert.Equal(2, pageResponse!.NextOffset);
        Assert.Equal(3, pageResponse.TotalCount);

        var singleItem = Assert.Single(pageResponse.Items);
        Assert.Equal("Task 2", singleItem.Title);
    }

    [Fact]
    public async Task CompleteTask_WhenRequestsAreConcurrent_OnlyOneSucceeds()
    {
        await ResetStateAsync();

        var setupClient = _factory.CreateClient();

        var createResponse = await setupClient.PostAsJsonAsync("/tasks", new CreateTaskRequest
        {
            Title = "Task for conflict"
        });

        createResponse.EnsureSuccessStatusCode();

        var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskItemResponse>();
        Assert.NotNull(createdTask);

        var firstClient = _factory.CreateClient();
        var secondClient = _factory.CreateClient();

        var firstCompleteTask = firstClient.PutAsync($"/tasks/{createdTask!.Id}/complete", content: null);
        var secondCompleteTask = secondClient.PutAsync($"/tasks/{createdTask.Id}/complete", content: null);

        var responses = await Task.WhenAll(firstCompleteTask, secondCompleteTask);
        var statusCodes = responses.Select(response => response.StatusCode).ToArray();

        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);
    }

    [Fact]
    public async Task CompleteTask_WhenPublisherFails_TaskIsStillCompleted()
    {
        await ResetStateAsync();

        var client = _factory.CreateClient();
        _factory.Publisher.ShouldThrow = true;

        var createResponse = await client.PostAsJsonAsync("/tasks", new CreateTaskRequest
        {
            Title = "Task with publisher failure"
        });

        createResponse.EnsureSuccessStatusCode();

        var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskItemResponse>();
        Assert.NotNull(createdTask);

        var completeResponse = await client.PutAsync($"/tasks/{createdTask!.Id}/complete", content: null);

        completeResponse.EnsureSuccessStatusCode();

        var completedTask = await completeResponse.Content.ReadFromJsonAsync<TaskItemResponse>();
        Assert.NotNull(completedTask);
        Assert.True(completedTask!.IsCompleted);
        Assert.NotNull(completedTask.CompletedAt);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        var persistedTask = await dbContext.Tasks.SingleAsync(task => task.Id == createdTask.Id);

        Assert.True(persistedTask.IsCompleted);
        Assert.NotNull(persistedTask.CompletedAt);
        Assert.Empty(_factory.Publisher.Events);
    }

    private async Task ResetStateAsync()
    {
        _factory.Publisher.Events.Clear();
        _factory.Publisher.ShouldThrow = false;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }
}

public sealed class TaskApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"task_api_tests_{Guid.NewGuid():N}";

    public FakeTaskEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TasksDb"] = $"Host=localhost;Port=5432;Database={_databaseName};Username=postgres;Password=postgres"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RabbitMqTaskEventPublisher>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ITaskEventPublisher>();
            services.AddSingleton<ITaskEventPublisher>(Publisher);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
    }
}

public sealed class FakeTaskEventPublisher : ITaskEventPublisher
{
    public List<TaskCompletedEvent> Events { get; } = [];
    public bool ShouldThrow { get; set; }

    public Task PublishTaskCompletedAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken)
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Publisher failure for test.");
        }

        Events.Add(taskCompletedEvent);
        return Task.CompletedTask;
    }
}
