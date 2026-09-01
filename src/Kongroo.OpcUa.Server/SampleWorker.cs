namespace Kongroo.OpcUa.Server;

/// <summary>Placeholder background service</summary>
public sealed class SampleWorker(ILogger<SampleWorker> logger, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogInformation("Worker ran at {Timestamp:o}", timeProvider.GetUtcNow());
        }
    }
}
