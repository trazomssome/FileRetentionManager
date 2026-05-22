using FileRetentionManager.App.ViewModels;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using Moq;

namespace FileRetentionManager.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task ExecuteNowCommand_DeletesMatchingFiles_AndGeneratesReport()
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
        var targetPathPickerService = new Mock<ITargetPathPickerService>();

        var viewModel = new MainViewModel(
            fileSystemService.Object,
            userDecisionService.Object,
            reportGenerator.Object,
            targetPathPickerService.Object)
        {
            MaximumAgeDays = 30,
            UseMinimumFileSize = true,
            MinimumFileSizeKb = 1024,
            NamePatternsText = "*.tmp"
        };
        viewModel.TargetPaths.Add(targetPath);

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        fileSystemService.Verify(
            service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()),
            Times.Once);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(ignoredFile.Path, It.IsAny<CancellationToken>()),
            Times.Never);
        reportGenerator.Verify(
            generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(reportPath, viewModel.LastReportPath);
        Assert.Contains(viewModel.Results, result => result.Path == matchedFile.Path && result.Status == "Deleted");
    }

    [Fact]
    public async Task ExecuteNowCommand_AsksBeforeStartingSequence()
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
            .ReturnsAsync(new ReportArtifact(@"C:\Reports\report.md", "# report"));
        var targetPathPickerService = new Mock<ITargetPathPickerService>();

        var viewModel = new MainViewModel(
            fileSystemService.Object,
            userDecisionService.Object,
            reportGenerator.Object,
            targetPathPickerService.Object)
        {
            MaximumAgeDays = 30,
            MinimumFileSizeKb = null,
            NamePatternsText = "*.log"
        };
        viewModel.TargetPaths.Add(targetPath);

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        userDecisionService.Verify(
            service => service.AskAsync(It.IsAny<SequenceStartRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fileSystemService.Verify(
            service => service.DeleteFileAsync(matchedFile.Path, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteNowCommand_DoesNotDelete_WhenSequenceStartIsRejected()
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
        var targetPathPickerService = new Mock<ITargetPathPickerService>();
        var viewModel = new MainViewModel(
            fileSystemService.Object,
            userDecisionService.Object,
            reportGenerator.Object,
            targetPathPickerService.Object)
        {
            MaximumAgeDays = 30,
            NamePatternsText = "*.log"
        };
        viewModel.TargetPaths.Add(targetPath);

        await viewModel.ExecuteNowCommand.ExecuteAsync(null);

        fileSystemService.Verify(
            service => service.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        reportGenerator.Verify(
            generator => generator.GenerateAsync(It.IsAny<RetentionCycleReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains(viewModel.Results, result => result.Path == matchedFile.Path && result.Status == "Preview");
    }

    [Fact]
    public async Task AddFolderTargetPathCommand_AddsSelectedFolder()
    {
        const string targetPath = @"C:\RetentionTarget";
        var fileSystemService = new Mock<IFileSystemService>();
        var userDecisionService = new Mock<IUserDecisionService>();
        var reportGenerator = new Mock<IReportGenerator>();
        var targetPathPickerService = new Mock<ITargetPathPickerService>();
        targetPathPickerService
            .Setup(service => service.PickFolderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPath);
        var viewModel = new MainViewModel(
            fileSystemService.Object,
            userDecisionService.Object,
            reportGenerator.Object,
            targetPathPickerService.Object);

        await viewModel.AddFolderTargetPathCommand.ExecuteAsync(null);

        Assert.Contains(targetPath, viewModel.TargetPaths);
        Assert.Equal(targetPath, viewModel.SelectedTargetPath);
    }
}
