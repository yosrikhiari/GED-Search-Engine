namespace GED.API.Services;

public interface IRabbitMqStatusProvider
{
    long GetQueueDepth();
}