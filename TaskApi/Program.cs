using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Endpoints;
using TaskApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqTaskEventPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(static sp => sp.GetRequiredService<RabbitMqTaskEventPublisher>());
builder.Services.AddHostedService(static sp => sp.GetRequiredService<RabbitMqTaskEventPublisher>());

builder.Services.AddDbContext<TaskDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("TasksDb");

    options.UseNpgsql(connectionString ?? "Host=localhost;Port=5432;Database=task_api;Username=postgres;Password=postgres");
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapTaskEndpoints();

app.Run();

public partial class Program;
