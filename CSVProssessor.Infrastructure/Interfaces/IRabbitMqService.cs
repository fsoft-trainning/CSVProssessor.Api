namespace CSVProssessor.Infrastructure.Interfaces
{
    public interface IRabbitMqService
    {
        /// <summary>
        /// Gửi message tới queue hoặc exchange (topic) cụ thể trong RabbitMQ
        /// </summary>
        /// <typeparam name="T">Kiểu dữ liệu của message</typeparam>
        /// <param name="destination">Tên queue hoặc exchange muốn gửi tới</param>
        /// <param name="message">Đối tượng message cần gửi (sẽ được JSON serialize)</param>
        /// <returns>Task đại diện cho thao tác bất đồng bộ</returns>
        Task PublishAsync<T>(string destination, T message);

        /// <summary>
        /// Gửi message tới topic (fanout exchange) - Phát tán cho nhiều subscriber cùng nhận
        /// </summary>
        /// <typeparam name="T">Kiểu dữ liệu của message</typeparam>
        /// <param name="topicName">Tên của topic (exchange)</param>
        /// <param name="message">Đối tượng message cần gửi (sẽ được JSON serialize)</param>
        /// <returns>Task đại diện cho thao tác bất đồng bộ</returns>
        Task PublishToTopicAsync<T>(string topicName, T message);
    }
}