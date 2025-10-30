using CSVProssessor.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace CSVProssessor.Infrastructure.Commons
{
    public class RabbitMqService : IRabbitMqService
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private readonly ILogger<RabbitMqService> _logger;

        public RabbitMqService(IConnection connection, ILogger<RabbitMqService> logger)
        {
            _connection = connection;
            _logger = logger;
            _channel = connection.CreateChannelAsync().Result;
        }

        /// <summary>
        /// Publishes a message to a specified RabbitMQ queue or exchange (topic).
        /// </summary>
        /// <typeparam name="T">The type of the message object.</typeparam>
        /// <param name="destination">The name of the queue or exchange.</param>
        /// <param name="message">The message object to be serialized and sent.</param>
        public async Task PublishAsync<T>(string destination, T message)
        {
            try
            {
                // Serialize message to JSON
                var jsonMessage = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                // Declare queue with durable option
                await _channel.QueueDeclareAsync(
                    queue: destination,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                // Publish message to queue
                var properties = new BasicProperties
                {
                    Persistent = true, // Make message persistent
                    ContentType = "application/json",
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                await _channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: destination,
                    mandatory: false,
                    basicProperties: properties,
                    body: new ReadOnlyMemory<byte>(body)
                );

                _logger.LogInformation($"Message published successfully to queue '{destination}'. Message: {jsonMessage}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing message to RabbitMQ queue '{destination}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Publishes a message to a RabbitMQ topic (fanout exchange) for broadcasting to multiple subscribers.
        /// </summary>
        /// <typeparam name="T">The type of the message object.</typeparam>
        /// <param name="topicName">The name of the topic (exchange).</param>
        /// <param name="message">The message object to be serialized and sent.</param>
        public async Task PublishToTopicAsync<T>(string topicName, T message)
        {
            try
            {
                // Serialize message to JSON
                var jsonMessage = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                // Declare fanout exchange
                await _channel.ExchangeDeclareAsync(
                    exchange: topicName,
                    type: ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    arguments: null
                );

                // Publish message to topic
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                await _channel.BasicPublishAsync(
                    exchange: topicName,
                    routingKey: string.Empty,
                    mandatory: false,
                    basicProperties: properties,
                    body: new ReadOnlyMemory<byte>(body)
                );

                _logger.LogInformation($"Message published successfully to topic '{topicName}'. Message: {jsonMessage}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing message to RabbitMQ topic '{topicName}': {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}