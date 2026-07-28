using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kart.Shared.Messaging;

/// <summary>
/// Shared reconnect/consume/retry-ladder-routing mechanics for an inbound event consumer: manual
/// ack/nack, a custom retry-count header, <c>BasicReject(requeue:false)</c> on final exhaustion
/// landing in the queue's own configured DLX. A service with more than one consumer queue derives
/// one subclass per queue — each differs only in which queue it consumes and how it
/// deserializes/dispatches its own payload.
/// </summary>
public abstract class RabbitMqConsumerHostedServiceBase : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

    private readonly IConnectionFactory _connectionFactory;
    private readonly MessageBusManifest _manifest;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _retryCountHeaderName;

    /// <param name="retryCountHeaderName">
    /// Custom header name this consumer stamps onto retried messages to track attempt count.
    /// Namespace it per service (e.g. <c>x-{service}-retry-count</c>) so retried messages from
    /// different services publishing onto shared infrastructure never collide.
    /// </param>
    protected RabbitMqConsumerHostedServiceBase(
        IConnectionFactory connectionFactory,
        MessageBusManifest manifest,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string retryCountHeaderName)
    {
        _connectionFactory = connectionFactory;
        _manifest = manifest;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retryCountHeaderName = retryCountHeaderName;
    }

    protected abstract string QueueName { get; }

    protected abstract Task ProcessAsync(ReadOnlyMemory<byte> body, IServiceProvider scopedProvider, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopologyProvisioner.Declare(channel, _manifest);
                channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
                channel.BasicConsume(QueueName, autoAck: false, consumer);

                await WaitWhileConnectedAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Queue} consumer lost its RabbitMQ connection; reconnecting in {Delay}.", QueueName, ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private static Task WaitWhileConnectedAsync(IConnection connection, CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdown += (_, _) => tcs.TrySetResult();
        using var registration = stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));
        return tcs.Task;
    }

    private async Task HandleDeliveryAsync(IModel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await ProcessAsync(delivery.Body, scope.ServiceProvider, stoppingToken);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message from {Queue} (delivery tag {DeliveryTag})", QueueName, delivery.DeliveryTag);
            RouteToNextRetryTierOrDlq(channel, delivery);
        }
    }

    private void RouteToNextRetryTierOrDlq(IModel channel, BasicDeliverEventArgs delivery)
    {
        var queue = _manifest.GetQueue(QueueName);
        var retryTiers = queue.RetryLadder?.Tiers ?? Array.Empty<RetryTierDefinition>();
        var attempt = GetRetryCount(delivery.BasicProperties) + 1;

        if (attempt > retryTiers.Count)
        {
            _logger.LogCritical(
                "{Queue} exhausted all {MaxAttempts} retry attempts for delivery tag {DeliveryTag}; routing to {Dlq}. Requires on-call attention.",
                QueueName, retryTiers.Count, delivery.DeliveryTag, queue.DeadLetter?.RoutingKey);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
            return;
        }

        var retryQueueName = retryTiers[attempt - 1].Name;
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = delivery.BasicProperties.ContentType;
        properties.MessageId = delivery.BasicProperties.MessageId;
        properties.Headers = new Dictionary<string, object> { [_retryCountHeaderName] = attempt };

        channel.BasicPublish(exchange: string.Empty, routingKey: retryQueueName, basicProperties: properties, body: delivery.Body);
        channel.BasicAck(delivery.DeliveryTag, multiple: false);
    }

    private int GetRetryCount(IBasicProperties properties)
    {
        if (properties.Headers is not null && properties.Headers.TryGetValue(_retryCountHeaderName, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.Parse(Encoding.UTF8.GetString(bytes)),
                _ => 0
            };
        }

        return 0;
    }
}
