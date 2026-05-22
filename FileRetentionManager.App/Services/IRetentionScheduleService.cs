namespace FileRetentionManager.App.Services;

public interface IRetentionScheduleService
{
    Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> executeAsync,
        CancellationToken cancellationToken);
}
