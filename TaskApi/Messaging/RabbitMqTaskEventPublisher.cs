using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskApi.Messaging;

/// <summary>
/// Публикатор событий задач в RabbitMQ.
/// </summary>
public sealed class RabbitMqTaskEventPublisher : ITaskEventPublisher, IHostedService, IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTaskEventPublisher> _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly SemaphoreSlim _inFlightSignal = new(0, int.MaxValue);
    private IConnection? _connection;
    private IChannel? _channel;
    private int _inFlightPublishes;

    public RabbitMqTaskEventPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTaskEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectionFactory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };
    }

    /// <summary>
    /// Пытается заранее открыть подключение и канал, но не останавливает приложение при ошибке.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureChannelAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RabbitMQ is unavailable during startup. The app will continue and retry on publish.");
        }
    }

    /// <summary>
    /// Публикует событие завершения задачи в exchange task.events.
    /// </summary>
    public async Task PublishTaskCompletedAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _inFlightPublishes);

        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                taskId = taskCompletedEvent.TaskId,
                title = taskCompletedEvent.Title,
                completedAt = taskCompletedEvent.CompletedAt,
                priority = taskCompletedEvent.Priority
            });

            await _publishLock.WaitAsync(cancellationToken);
            try
            {
                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: _options.RoutingKey,
                    mandatory: false,
                    body: body,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                _publishLock.Release();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlightPublishes) == 0)
            {
                _inFlightSignal.Release();
            }
        }
    }

    /// <summary>
    /// Лениво инициализирует канал RabbitMQ при старте или первой публикации.
    /// </summary>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            return _channel;
        }

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null)
            {
                return _channel;
            }

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    /// <summary>
    /// Дает уже начатым публикациям шанс корректно завершиться при остановке приложения.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _inFlightPublishes) > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await _inFlightSignal.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("RabbitMQ shutdown timeout reached before all publishes completed.");
            }
        }

        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Освобождает ресурсы RabbitMQ и внутренние примитивы синхронизации.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _publishLock.Dispose();
        _inFlightSignal.Dispose();

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
