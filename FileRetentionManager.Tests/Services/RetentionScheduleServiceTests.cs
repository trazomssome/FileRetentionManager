using FileRetentionManager.App.Services;

namespace FileRetentionManager.Tests.Services;

public sealed class RetentionScheduleServiceTests
{
    [Fact]
    public async Task RunAsync_ExecutesCallbackOnTimerTick()
    {
        var service = new RetentionScheduleService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var callbackInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = service.RunAsync(
            TimeSpan.FromMilliseconds(1),
            _ =>
            {
                callbackInvoked.SetResult();
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await callbackInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task RunAsync_PropagatesCallbackExceptions()
    {
        var service = new RetentionScheduleService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(
                TimeSpan.FromMilliseconds(1),
                _ => throw new InvalidOperationException("Scheduled cycle failed."),
                cancellation.Token));

        Assert.Equal("Scheduled cycle failed.", exception.Message);
    }
}
