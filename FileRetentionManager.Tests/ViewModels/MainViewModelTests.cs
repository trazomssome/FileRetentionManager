using FileRetentionManager.App.Services;
using FileRetentionManager.App.Validation;
using FileRetentionManager.App.ViewModels;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using Moq;

namespace FileRetentionManager.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void ValidatedProperties_HaveFluentValidationAttributesOnGeneratedProperties()
    {
        var propertyNames = typeof(MainViewModel)
            .GetProperties()
            .Where(property => Attribute.IsDefined(property, typeof(FluentValidationPropertyAttribute)))
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains(nameof(MainViewModel.ScheduleHours), propertyNames);
        Assert.Contains(nameof(MainViewModel.ScheduleMinutes), propertyNames);
        Assert.Contains(nameof(MainViewModel.ScheduleSeconds), propertyNames);
        Assert.Contains(nameof(MainViewModel.MaximumAgeDays), propertyNames);
        Assert.Contains(nameof(MainViewModel.MinimumFileSizeKb), propertyNames);
        Assert.Contains(nameof(MainViewModel.NamePatternsText), propertyNames);
        Assert.Contains(nameof(MainViewModel.TargetPaths), propertyNames);
    }

    [Fact]
    public async Task ExecuteNowCommand_SetsErrors_WhenScheduleIntervalIsZero()
    {
        var viewModel = CreateViewModel();
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");
        viewModel.ScheduleHours = 0;
        viewModel.ScheduleMinutes = 0;
        viewModel.ScheduleSeconds = 0;

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Contains("Schedule interval must be greater than zero.", viewModel.ValidationSummary);
    }

    [Fact]
    public async Task ExecuteNowCommand_StopsBeforeCallingSequence_WhenTargetPathIsMissing()
    {
        var sequenceService = new Mock<IRetentionSequenceService>(MockBehavior.Strict);
        var viewModel = CreateViewModel(sequenceService);

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Contains("At least one target path is required.", viewModel.ValidationSummary);
        sequenceService.Verify(
            service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteNowCommand_SetsErrors_WhenAllDeletionOptionsAreDisabled()
    {
        var viewModel = CreateViewModel();
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");
        viewModel.UseMaximumAge = false;
        viewModel.UseMinimumFileSize = false;
        viewModel.UseNamePatterns = false;

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Contains("At least one deletion option must be enabled.", viewModel.ValidationSummary);
    }

    [Fact]
    public async Task ExecuteNowCommand_SetsErrors_WhenEnabledOptionsHaveInvalidValues()
    {
        var viewModel = CreateViewModel();
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");
        viewModel.UseMaximumAge = true;
        viewModel.MaximumAgeDays = null;
        viewModel.UseMinimumFileSize = true;
        viewModel.MinimumFileSizeKb = 0;
        viewModel.UseNamePatterns = true;
        viewModel.NamePatternsText = string.Empty;

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Contains("Maximum age is required when the option is enabled.", viewModel.ValidationSummary);
        Assert.Contains("Minimum size must be greater than zero.", viewModel.ValidationSummary);
        Assert.Contains("At least one name pattern is required when the option is enabled.", viewModel.ValidationSummary);
    }

    [Fact]
    public async Task TargetPaths_CollectionChanges_UpdateValidationErrors()
    {
        var viewModel = CreateViewModel();

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Contains("At least one target path is required.", viewModel.ValidationSummary);

        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        Assert.DoesNotContain("At least one target path is required.", viewModel.ValidationSummary);

        viewModel.TargetPaths.Clear();

        Assert.Contains("At least one target path is required.", viewModel.ValidationSummary);
    }

    [Fact]
    public async Task ExecuteNowCommand_AppliesCompletedSequenceResult()
    {
        const string targetPath = @"C:\RetentionTarget";
        const string reportPath = @"C:\Reports\report.md";
        var completedAt = DateTimeOffset.UtcNow;
        var matchedFile = new FileMetadata(
            @"C:\RetentionTarget\old.tmp",
            "old.tmp",
            2048,
            completedAt.AddDays(-60),
            completedAt.AddDays(-45));
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.Is<RetentionSettingsDraft>(draft => draft.TargetPaths.Contains(targetPath)),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetentionSequenceResult(
                RetentionSequenceStatus.Completed,
                completedAt,
                new ReportArtifact(reportPath, "# report"),
                [matchedFile],
                [new DeletionResult(matchedFile.Path, true, null)],
                []));
        var viewModel = CreateViewModel(sequenceService);
        viewModel.TargetPaths.Add(targetPath);

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.Equal(reportPath, viewModel.LastReportPath);
        Assert.Equal($"Last run: {completedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}", viewModel.LastRunText);
        Assert.Equal("Cycle completed. 1 matched, 1 deleted, 0 failed.", viewModel.StatusMessage);
        Assert.Contains(viewModel.Results, result => result.Path == matchedFile.Path && result.Status == "Deleted");
    }

    [Fact]
    public async Task ExecuteNowCommand_PromptsWhenCallingSequence()
    {
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetentionSequenceResult(
                RetentionSequenceStatus.Completed,
                DateTimeOffset.UtcNow,
                new ReportArtifact(@"C:\Reports\report.md", "# report"),
                [],
                [],
                []));
        var viewModel = CreateViewModel(sequenceService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        sequenceService.Verify(
            service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteNowCommand_AppliesRejectedSequenceResult()
    {
        var now = DateTimeOffset.UtcNow;
        var matchedFile = new FileMetadata(
            @"C:\RetentionTarget\old.log",
            "old.log",
            512,
            now.AddDays(-60),
            now.AddDays(-45));
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetentionSequenceResult(
                RetentionSequenceStatus.Rejected,
                null,
                null,
                [matchedFile],
                [],
                []));
        var viewModel = CreateViewModel(sequenceService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.Equal("Sequence start cancelled. 1 files were not deleted.", viewModel.StatusMessage);
        Assert.Contains(viewModel.Results, result => result.Path == matchedFile.Path && result.Status == "Preview");
    }

    [Fact]
    public async Task ExecuteNowCommand_AppliesAlreadyRunningSequenceResult()
    {
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RetentionSequenceResult.AlreadyRunning);
        var viewModel = CreateViewModel(sequenceService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.Equal("A retention cycle is already running.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExecuteNowCommand_AppliesCancelledSequenceResult()
    {
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RetentionSequenceResult.Cancelled);
        var viewModel = CreateViewModel(sequenceService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        Assert.Equal("Retention cycle cancelled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleActivationCommand_StartsSchedule_AfterInitialSequenceCompletes()
    {
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetentionSequenceResult(
                RetentionSequenceStatus.Completed,
                DateTimeOffset.UtcNow,
                new ReportArtifact(@"C:\Reports\report.md", "# report"),
                [],
                [],
                []));
        var scheduleService = new Mock<IRetentionScheduleService>();
        scheduleService
            .Setup(service => service.RunAsync(
                TimeSpan.FromHours(24),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(sequenceService, scheduleService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ToggleActivationCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRetentionEnabled);
        Assert.Equal("Disable", viewModel.ActivationButtonText);
        scheduleService.Verify(
            service => service.RunAsync(
                TimeSpan.FromHours(24),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ToggleActivationCommand_DisablesSchedule_AndWaitsForCancellation()
    {
        var scheduleStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sequenceService = new Mock<IRetentionSequenceService>();
        sequenceService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<RetentionSettingsDraft>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetentionSequenceResult(
                RetentionSequenceStatus.Completed,
                DateTimeOffset.UtcNow,
                new ReportArtifact(@"C:\Reports\report.md", "# report"),
                [],
                [],
                []));
        var scheduleService = new Mock<IRetentionScheduleService>();
        scheduleService
            .Setup(service => service.RunAsync(
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, Func<CancellationToken, Task>, CancellationToken>(
                async (_, _, cancellationToken) =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        scheduleStopped.SetResult();
                        throw;
                    }
                });
        var viewModel = CreateViewModel(sequenceService, scheduleService);
        viewModel.TargetPaths.Add(@"C:\RetentionTarget");

        await viewModel.ToggleActivationCommand.ExecuteAsync(null);
        await viewModel.ToggleActivationCommand.ExecuteAsync(null);

        await scheduleStopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(viewModel.IsRetentionEnabled);
        Assert.Equal("Enable", viewModel.ActivationButtonText);
        Assert.Equal("Retention is disabled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddFolderTargetPathCommand_AddsSelectedFolder()
    {
        const string targetPath = @"C:\RetentionTarget";
        var targetPathPickerService = new Mock<ITargetPathPickerService>();
        targetPathPickerService
            .Setup(service => service.PickFolderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPath);
        var viewModel = CreateViewModel(targetPathPickerService: targetPathPickerService);

        await viewModel.AddFolderTargetPathCommand.ExecuteAsync(null);

        Assert.Contains(targetPath, viewModel.TargetPaths);
        Assert.Equal(targetPath, viewModel.SelectedTargetPath);
    }

    private static MainViewModel CreateViewModel(
        Mock<IRetentionSequenceService>? sequenceService = null,
        Mock<IRetentionScheduleService>? scheduleService = null,
        Mock<ITargetPathPickerService>? targetPathPickerService = null)
    {
        return new MainViewModel(
            (sequenceService ?? new Mock<IRetentionSequenceService>()).Object,
            (scheduleService ?? new Mock<IRetentionScheduleService>()).Object,
            (targetPathPickerService ?? new Mock<ITargetPathPickerService>()).Object);
    }
}
