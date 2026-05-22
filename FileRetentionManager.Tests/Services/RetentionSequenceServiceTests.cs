using FileRetentionManager.App.Services;
using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Rules;
using FileRetentionManager.Domain.Services;
using Moq;

namespace FileRetentionManager.Tests.Services;

public sealed class RetentionSequenceServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DeletesMatchingFiles_AndGeneratesReport_WhenApproved()
    {
        const string targetPath = @"C:\RetentionTarget";
        const string reportPath = @"C:\Reports\report.md";
        var now = DateTimeOffset.UtcNow;
        var matchedFile = new FileMetadata(
            @"C:\RetentionTarget\old.tmp",
            "old.tmp",
            2 * 1024 * 1024,
            now.AddDays(-60),
            now.AddDays(-45));
        var ignoredFile = new FileMetadata(
            @"C:\RetentionTarget\fresh.tmp",
            "fresh.tmp",
            2 * 1024 * 1024,
            now.AddDays(-2),
            now.AddDays(-1));
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(targetPath))
            .Returns(true);
        fileSystemService
            .Setup(service => service.FileExists(targetPath))
            .Returns(false);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(targetPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchedFile, ignoredFile]);
        fileSystemService
            .Setup(service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var userDecisionService = new Mock<IUserDecisionService>();
        userDecisionService
            .Setup(service => service.AskAsync(It.IsAny<SequenceStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserDecision.Approved);
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator
            .Setup(generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportArtifact(reportPath, "# report"));
        var service = CreateService(fileSystemService, userDecisionService, reportGenerator);

        var result = await service.ExecuteAsync(CreateDraft(targetPath), true, CancellationToken.None);

        Assert.Equal(RetentionSequenceStatus.Completed, result.Status);
        Assert.Equal(reportPath, result.Report?.Path);
        Assert.Contains(result.Candidates, candidate => candidate.Path == matchedFile.Path);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Path == ignoredFile.Path);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()),
            Times.Once);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(ignoredFile.Path, It.IsAny<CancellationToken>()),
            Times.Never);
        reportGenerator.Verify(
            generator => generator.GenerateAsync(
                It.Is<RetentionCycleReport>(report =>
                    report.WasSequenceStartPrompted &&
                    report.WasSequenceStartApproved &&
                    report.Candidates.Count == 1 &&
                    report.Candidates[0].Path == matchedFile.Path),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotDeleteOrReport_WhenSequenceStartIsRejected()
    {
        const string targetPath = @"C:\RetentionTarget";
        var now = DateTimeOffset.UtcNow;
        var matchedFile = new FileMetadata(
            @"C:\RetentionTarget\old.log",
            "old.log",
            512,
            now.AddDays(-60),
            now.AddDays(-45));
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(targetPath))
            .Returns(true);
        fileSystemService
            .Setup(service => service.FileExists(targetPath))
            .Returns(false);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(targetPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchedFile]);
        var userDecisionService = new Mock<IUserDecisionService>();
        userDecisionService
            .Setup(service => service.AskAsync(It.IsAny<SequenceStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserDecision.Rejected);
        var reportGenerator = new Mock<IReportGenerator>();
        var service = CreateService(fileSystemService, userDecisionService, reportGenerator);

        var result = await service.ExecuteAsync(CreateDraft(targetPath), true, CancellationToken.None);

        Assert.Equal(RetentionSequenceStatus.Rejected, result.Status);
        Assert.Contains(result.Candidates, candidate => candidate.Path == matchedFile.Path);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        reportGenerator.Verify(
            generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesReportWithNoMatchNote_WhenNoFilesMatch()
    {
        const string targetPath = @"C:\RetentionTarget";
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(targetPath))
            .Returns(true);
        fileSystemService
            .Setup(service => service.FileExists(targetPath))
            .Returns(false);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(targetPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator
            .Setup(generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportArtifact(@"C:\Reports\report.md", "# report"));
        var service = CreateService(fileSystemService, reportGenerator: reportGenerator);

        var result = await service.ExecuteAsync(CreateDraft(targetPath), false, CancellationToken.None);

        Assert.Equal(RetentionSequenceStatus.Completed, result.Status);
        Assert.Contains("No files matched the active retention criteria.", result.Notes);
        reportGenerator.Verify(
            generator => generator.GenerateAsync(
                It.Is<RetentionCycleReport>(report =>
                    report.Candidates.Count == 0 &&
                    report.Notes.Contains("No files matched the active retention criteria.")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsNoteAndSkipsScan_WhenTargetPathDoesNotExist()
    {
        const string targetPath = @"C:\MissingTarget";
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(targetPath))
            .Returns(false);
        fileSystemService
            .Setup(service => service.FileExists(targetPath))
            .Returns(false);
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator
            .Setup(generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportArtifact(@"C:\Reports\report.md", "# report"));
        var service = CreateService(fileSystemService, reportGenerator: reportGenerator);

        var result = await service.ExecuteAsync(CreateDraft(targetPath), false, CancellationToken.None);

        Assert.Equal(RetentionSequenceStatus.Completed, result.Status);
        Assert.Contains($"Target path does not exist: `{targetPath}`.", result.Notes);
        fileSystemService.Verify(
            service => service.EnumerateFilesAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesDuplicateCandidatePathOnlyOnce()
    {
        const string firstTargetPath = @"C:\RetentionTargetA";
        const string secondTargetPath = @"C:\RetentionTargetB";
        var now = DateTimeOffset.UtcNow;
        var matchedFile = new FileMetadata(
            @"C:\Shared\old.log",
            "old.log",
            512,
            now.AddDays(-60),
            now.AddDays(-45));
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(It.IsAny<string>()))
            .Returns(true);
        fileSystemService
            .Setup(service => service.FileExists(It.IsAny<string>()))
            .Returns(false);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(firstTargetPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchedFile]);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(secondTargetPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchedFile]);
        fileSystemService
            .Setup(service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator
            .Setup(generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportArtifact(@"C:\Reports\report.md", "# report"));
        var service = CreateService(fileSystemService, reportGenerator: reportGenerator);

        var result = await service.ExecuteAsync(
            CreateDraft(firstTargetPath, secondTargetPath),
            false,
            CancellationToken.None);

        Assert.Equal(RetentionSequenceStatus.Completed, result.Status);
        Assert.Single(result.Candidates);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAlreadyRunning_WhenAnotherSequenceIsActive()
    {
        const string targetPath = @"C:\RetentionTarget";
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScan = new TaskCompletionSource<IReadOnlyList<FileMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(service => service.DirectoryExists(targetPath))
            .Returns(true);
        fileSystemService
            .Setup(service => service.FileExists(targetPath))
            .Returns(false);
        fileSystemService
            .Setup(service => service.EnumerateFilesAsync(targetPath, true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                scanStarted.SetResult();
                return await releaseScan.Task;
            });
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator
            .Setup(generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportArtifact(@"C:\Reports\report.md", "# report"));
        var service = CreateService(fileSystemService, reportGenerator: reportGenerator);
        var draft = CreateDraft(targetPath);

        var firstRun = service.ExecuteAsync(draft, false, CancellationToken.None);
        await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondResult = await service.ExecuteAsync(draft, false, CancellationToken.None);
        releaseScan.SetResult([]);
        var firstResult = await firstRun;

        Assert.Equal(RetentionSequenceStatus.AlreadyRunning, secondResult.Status);
        Assert.Equal(RetentionSequenceStatus.Completed, firstResult.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCancelled_WhenCancellationIsRequested()
    {
        var fileSystemService = new Mock<IFileSystemService>(MockBehavior.Strict);
        var userDecisionService = new Mock<IUserDecisionService>(MockBehavior.Strict);
        var reportGenerator = new Mock<IReportGenerator>(MockBehavior.Strict);
        var service = CreateService(fileSystemService, userDecisionService, reportGenerator);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await service.ExecuteAsync(CreateDraft(@"C:\RetentionTarget"), false, cancellation.Token);

        Assert.Equal(RetentionSequenceStatus.Cancelled, result.Status);
    }

    private static RetentionSequenceService CreateService(
        Mock<IFileSystemService>? fileSystemService = null,
        Mock<IUserDecisionService>? userDecisionService = null,
        Mock<IReportGenerator>? reportGenerator = null)
    {
        return new RetentionSequenceService(
            (fileSystemService ?? new Mock<IFileSystemService>()).Object,
            (userDecisionService ?? new Mock<IUserDecisionService>()).Object,
            (reportGenerator ?? new Mock<IReportGenerator>()).Object,
            CompositeRetentionRule.Default);
    }

    private static RetentionSettingsDraft CreateDraft(params string[] targetPaths)
    {
        return new RetentionSettingsDraft(
            0,
            1,
            0,
            targetPaths,
            true,
            true,
            30,
            false,
            1,
            true,
            ["*.tmp", "*.log"],
            ConditionJoinMode.And);
    }
}
