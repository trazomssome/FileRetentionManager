using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Rules;
using FileRetentionManager.Domain.Services;
using FluentValidation.Results;

namespace FileRetentionManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly char[] PatternSeparators = ['\r', '\n', ';', ','];

    private readonly IFileSystemService fileSystemService;
    private readonly IUserDecisionService userDecisionService;
    private readonly IReportGenerator reportGenerator;
    private readonly ITargetPathPickerService targetPathPickerService;
    private readonly RetentionSettingsValidator validator = new();
    private readonly CompositeRetentionRule retentionRule = CompositeRetentionRule.Default;
    private readonly SemaphoreSlim executionLock = new(1, 1);
    private readonly SynchronizationContext? synchronizationContext;

    private CancellationTokenSource? scheduleCancellation;

    public MainViewModel(
        IFileSystemService fileSystemService,
        IUserDecisionService userDecisionService,
        IReportGenerator reportGenerator,
        ITargetPathPickerService targetPathPickerService)
    {
        this.fileSystemService = fileSystemService;
        this.userDecisionService = userDecisionService;
        this.reportGenerator = reportGenerator;
        this.targetPathPickerService = targetPathPickerService;
        synchronizationContext = SynchronizationContext.Current;
    }

    [ObservableProperty]
    private bool isRetentionEnabled;

    [ObservableProperty]
    private string activationButtonText = "Enable";

    [ObservableProperty]
    private int scheduleHours = 24;

    [ObservableProperty]
    private int scheduleMinutes;

    [ObservableProperty]
    private int scheduleSeconds;

    [ObservableProperty]
    private bool includeSubdirectories = true;

    [ObservableProperty]
    private ConditionJoinMode conditionMode = ConditionJoinMode.And;

    [ObservableProperty]
    private bool useMaximumAge = true;

    [ObservableProperty]
    private double? maximumAgeDays = 30;

    [ObservableProperty]
    private bool useMinimumFileSize;

    [ObservableProperty]
    private double? minimumFileSizeKb = 1;

    [ObservableProperty]
    private bool useNamePatterns = true;

    [ObservableProperty]
    private string namePatternsText = "*.tmp;*.log";

    [ObservableProperty]
    private string? selectedTargetPath;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string validationSummary = string.Empty;

    [ObservableProperty]
    private string lastReportPath = "No report has been generated yet.";

    [ObservableProperty]
    private DateTimeOffset? lastRunAt;

    [ObservableProperty]
    private string lastRunText = "No cycle has run yet.";

    public IReadOnlyList<ConditionJoinMode> ConditionModes { get; } =
        Enum.GetValues<ConditionJoinMode>();

    public ObservableCollection<string> TargetPaths { get; } = [];

    public ObservableCollection<DeletionResultViewModel> Results { get; } = [];

    [RelayCommand]
    private async Task AddFolderTargetPathAsync()
    {
        var selectedPath = await targetPathPickerService.PickFolderAsync(CancellationToken.None);
        AddTargetPath(selectedPath);
    }

    [RelayCommand]
    private void RemoveSelectedTargetPath()
    {
        if (SelectedTargetPath is null)
        {
            return;
        }

        TargetPaths.Remove(SelectedTargetPath);
        SelectedTargetPath = null;
    }

    [RelayCommand]
    private async Task ToggleActivationAsync()
    {
        if (IsRetentionEnabled)
        {
            StopSchedule();
            IsRetentionEnabled = false;
            ActivationButtonText = "Enable";
            StatusMessage = "Retention is disabled.";
            return;
        }

        var draft = BuildDraft();
        var sequenceStarted = await ExecuteCycleAsync(draft, CancellationToken.None, promptForSequenceStart: true);

        if (!sequenceStarted)
        {
            return;
        }

        IsRetentionEnabled = true;
        ActivationButtonText = "Disable";
        StatusMessage = $"Retention is enabled. {StatusMessage}";
        scheduleCancellation = new CancellationTokenSource();
        _ = RunScheduleAsync(draft, scheduleCancellation.Token);
    }

    [RelayCommand]
    private async Task ExecuteNowAsync()
    {
        await ExecuteCycleAsync(BuildDraft(), CancellationToken.None, promptForSequenceStart: true);
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        StatusMessage = "Results cleared.";
    }

    private async Task RunScheduleAsync(RetentionSettingsDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(draft.ScheduleInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await ExecuteCycleAsync(draft, cancellationToken, promptForSequenceStart: false);
            }
        }
        catch (OperationCanceledException)
        {
            await DispatchAsync(() => StatusMessage = "Retention is disabled.");
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                IsRetentionEnabled = false;
                ActivationButtonText = "Enable";
                StatusMessage = $"Scheduler stopped: {exception.Message}";
            });
        }
    }

    private async Task<bool> ExecuteCycleAsync(
        RetentionSettingsDraft draft,
        CancellationToken cancellationToken,
        bool promptForSequenceStart)
    {
        if (!await executionLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            await DispatchAsync(() => StatusMessage = "A retention cycle is already running.");
            return false;
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var notes = new List<string>();

        try
        {
            await DispatchAsync(() =>
            {
                IsBusy = true;
                Results.Clear();
                StatusMessage = "Scanning files.";
            });

            var validation = validator.Validate(draft);

            await DispatchAsync(() => ApplyValidation(validation));

            if (!validation.IsValid)
            {
                await DispatchAsync(() => StatusMessage = "Settings need attention.");
                return false;
            }

            var criteria = BuildCriteria(draft, startedAtUtc);
            var candidates = await FindCandidatesAsync(draft.TargetPaths, criteria, notes, cancellationToken);

            if (promptForSequenceStart)
            {
                var request = new SequenceStartRequest(candidates, criteria, draft.TargetPaths);
                var decision = await userDecisionService.AskAsync(request, cancellationToken);

                if (decision != UserDecision.Approved)
                {
                    await DispatchAsync(() => ApplySequenceStartRejected(candidates));
                    return false;
                }
            }

            var deletionResults = candidates.Count > 0
                ? await DeleteCandidatesAsync(candidates, cancellationToken)
                : [];

            if (candidates.Count == 0)
            {
                notes.Add("No files matched the active retention criteria.");
            }

            var completedAtUtc = DateTimeOffset.UtcNow;
            var report = new RetentionCycleReport(
                Guid.NewGuid().ToString("N")[..8],
                startedAtUtc,
                completedAtUtc,
                criteria,
                draft.TargetPaths,
                true,
                promptForSequenceStart,
                candidates,
                deletionResults,
                notes);

            var artifact = await reportGenerator.GenerateAsync(report, cancellationToken);
            await DispatchAsync(() => ApplyCycleResult(completedAtUtc, artifact, candidates, deletionResults));
            return true;
        }
        catch (OperationCanceledException)
        {
            await DispatchAsync(() => StatusMessage = "Retention cycle cancelled.");
            return false;
        }
        finally
        {
            await DispatchAsync(() => IsBusy = false);
            executionLock.Release();
        }
    }

    private async Task<IReadOnlyList<FileMetadata>> FindCandidatesAsync(
        IReadOnlyList<string> targetPaths,
        RetentionCriteria criteria,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var candidates = new List<FileMetadata>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetPath in targetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!fileSystemService.DirectoryExists(targetPath) && !fileSystemService.FileExists(targetPath))
            {
                notes.Add($"Target path does not exist: `{targetPath}`.");
                continue;
            }

            try
            {
                var files = await fileSystemService.EnumerateFilesAsync(
                    targetPath,
                    criteria.IncludeSubdirectories,
                    cancellationToken);

                foreach (var file in files.Where(file => retentionRule.IsMatch(file, criteria)))
                {
                    if (seenPaths.Add(file.Path))
                    {
                        candidates.Add(file);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                notes.Add($"Target path could not be scanned: `{targetPath}`. {exception.Message}");
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<DeletionResult>> DeleteCandidatesAsync(
        IReadOnlyList<FileMetadata> candidates,
        CancellationToken cancellationToken)
    {
        var results = new List<DeletionResult>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await fileSystemService.DeleteFileAsync(candidate.Path, cancellationToken);
                results.Add(new DeletionResult(candidate.Path, true, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new DeletionResult(candidate.Path, false, exception.Message));
            }
        }

        return results;
    }

    private void ApplyCycleResult(
        DateTimeOffset completedAt,
        ReportArtifact artifact,
        IReadOnlyList<FileMetadata> candidates,
        IReadOnlyList<DeletionResult> deletionResults)
    {
        Results.Clear();

        foreach (var result in deletionResults)
        {
            Results.Add(new DeletionResultViewModel(
                result.Path,
                result.Succeeded ? "Deleted" : "Failed",
                result.ErrorMessage ?? "Completed."));
        }

        LastRunAt = completedAt.ToLocalTime();
        LastRunText = $"Last run: {LastRunAt.Value:yyyy-MM-dd HH:mm:ss zzz}";
        LastReportPath = artifact.Path;
        StatusMessage = BuildStatus(candidates, deletionResults);
    }

    private void ApplySequenceStartRejected(IReadOnlyList<FileMetadata> candidates)
    {
        Results.Clear();

        foreach (var candidate in candidates)
        {
            Results.Add(new DeletionResultViewModel(
                candidate.Path,
                "Preview",
                "Sequence was not started."));
        }

        StatusMessage = $"Sequence start cancelled. {candidates.Count} files were not deleted.";
    }

    private static string BuildStatus(
        IReadOnlyList<FileMetadata> candidates,
        IReadOnlyList<DeletionResult> deletionResults)
    {
        var deleted = deletionResults.Count(result => result.Succeeded);
        var failed = deletionResults.Count(result => !result.Succeeded);
        return $"Cycle completed. {candidates.Count} matched, {deleted} deleted, {failed} failed.";
    }

    private void AddTargetPath(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) ||
            TargetPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        TargetPaths.Add(selectedPath);
        SelectedTargetPath = selectedPath;
    }

    private RetentionSettingsDraft BuildDraft()
    {
        return new RetentionSettingsDraft(
            ScheduleHours,
            ScheduleMinutes,
            ScheduleSeconds,
            TargetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            IncludeSubdirectories,
            UseMaximumAge,
            MaximumAgeDays,
            UseMinimumFileSize,
            MinimumFileSizeKb,
            UseNamePatterns,
            Split(NamePatternsText, PatternSeparators),
            ConditionMode);
    }

    private static RetentionCriteria BuildCriteria(RetentionSettingsDraft draft, DateTimeOffset nowUtc)
    {
        var olderThanUtc = draft.UseMaximumAge && draft.MaximumAgeDays.HasValue
            ? nowUtc.AddDays(-draft.MaximumAgeDays.Value)
            : (DateTimeOffset?)null;
        var minimumSizeBytes = draft.UseMinimumFileSize && draft.MinimumFileSizeKb.HasValue
            ? (long)(draft.MinimumFileSizeKb.Value * 1024)
            : (long?)null;
        var namePatterns = draft.UseNamePatterns
            ? draft.NamePatterns
            : [];

        return new RetentionCriteria(
            olderThanUtc,
            minimumSizeBytes,
            namePatterns,
            draft.TargetPaths,
            draft.IncludeSubdirectories,
            draft.ConditionMode);
    }

    private static IReadOnlyList<string> Split(string value, char[] separators)
    {
        return value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void ApplyValidation(ValidationResult validation)
    {
        ValidationSummary = validation.IsValid
            ? string.Empty
            : string.Join(Environment.NewLine, validation.Errors.Select(error => error.ErrorMessage));
    }

    private Task DispatchAsync(Action action)
    {
        if (synchronizationContext is null || SynchronizationContext.Current == synchronizationContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        synchronizationContext.Post(
            _ =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            null);

        return completion.Task;
    }

    private void StopSchedule()
    {
        scheduleCancellation?.Cancel();
        scheduleCancellation?.Dispose();
        scheduleCancellation = null;
    }
}
