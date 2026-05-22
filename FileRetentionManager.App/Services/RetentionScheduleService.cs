namespace FileRetentionManager.App.Services;

public sealed class RetentionScheduleService : IRetentionScheduleService
{
    public async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> executeAsync,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await executeAsync(cancellationToken);
        }
    }
}
