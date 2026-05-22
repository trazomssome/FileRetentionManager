using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Rules;
using FileRetentionManager.Domain.Services;
using FluentValidation;

namespace FileRetentionManager.App.ViewModels;

public partial class MainViewModel : ObservableValidator, IRetentionSettingsValidationSource
{
    private static readonly char[] PatternSeparators = ['\r', '\n', ';', ','];
    private static readonly string[] ValidatedPropertyNames =
    [
        nameof(ScheduleHours),
        nameof(ScheduleMinutes),
        nameof(ScheduleSeconds),
        nameof(TargetPaths),
        nameof(UseMaximumAge),
        nameof(MaximumAgeDays),
        nameof(UseMinimumFileSize),
        nameof(MinimumFileSizeKb),
        nameof(UseNamePatterns),
        nameof(NamePatternsText)
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationRuleNamesByProperty =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [nameof(ScheduleHours)] =
            [
                nameof(RetentionSettingsDraft.ScheduleHours),
                nameof(RetentionSettingsDraft.ScheduleInterval)
            ],
            [nameof(ScheduleMinutes)] =
            [
                nameof(RetentionSettingsDraft.ScheduleMinutes),
                nameof(RetentionSettingsDraft.ScheduleInterval)
            ],
            [nameof(ScheduleSeconds)] =
            [
                nameof(RetentionSettingsDraft.ScheduleSeconds),
                nameof(RetentionSettingsDraft.ScheduleInterval)
            ],
            [nameof(TargetPaths)] = [nameof(RetentionSettingsDraft.TargetPaths)],
            [nameof(UseMaximumAge)] =
            [
                nameof(RetentionSettingsDraft.HasAnyDeletionCondition)
            ],
            [nameof(MaximumAgeDays)] = [nameof(RetentionSettingsDraft.MaximumAgeDays)],
            [nameof(UseMinimumFileSize)] =
            [
                nameof(RetentionSettingsDraft.HasAnyDeletionCondition)
            ],
            [nameof(MinimumFileSizeKb)] = [nameof(RetentionSettingsDraft.MinimumFileSizeKb)],
            [nameof(UseNamePatterns)] =
            [
                nameof(RetentionSettingsDraft.HasAnyDeletionCondition)
            ],
            [nameof(NamePatternsText)] = [nameof(RetentionSettingsDraft.NamePatterns)]
        };

    private readonly IFileSystemService fileSystemService;
    private readonly IUserDecisionService userDecisionService;
    private readonly IReportGenerator reportGenerator;
    private readonly ITargetPathPickerService targetPathPickerService;
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
        Validator = new RetentionSettingsValidator();
        synchronizationContext = SynchronizationContext.Current;
        TargetPaths.CollectionChanged += OnTargetPathsChanged;
        ErrorsChanged += (_, _) => OnPropertyChanged(nameof(ValidationSummary));
    }

    public IValidator<RetentionSettingsDraft> Validator { get; }

    [ObservableProperty]
    private bool isRetentionEnabled;

    [ObservableProperty]
    private string activationButtonText = "Enable";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private int scheduleHours = 24;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private int scheduleMinutes;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private int scheduleSeconds;

    [ObservableProperty]
    private bool includeSubdirectories = true;

    [ObservableProperty]
    private ConditionJoinMode conditionMode = ConditionJoinMode.And;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private bool useMaximumAge = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private double? maximumAgeDays = 30;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private bool useMinimumFileSize;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private double? minimumFileSizeKb = 1;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private bool useNamePatterns = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [FluentValidationProperty]
    private string namePatternsText = "*.tmp;*.log";

    [ObservableProperty]
    private string? selectedTargetPath;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string lastReportPath = "No report has been generated yet.";

    [ObservableProperty]
    private DateTimeOffset? lastRunAt;

    [ObservableProperty]
    private string lastRunText = "No cycle has run yet.";

    public IReadOnlyList<ConditionJoinMode> ConditionModes { get; } =
        Enum.GetValues<ConditionJoinMode>();

    [FluentValidationProperty]
    public ObservableCollection<string> TargetPaths { get; } = [];

    public ObservableCollection<DeletionResultViewModel> Results { get; } = [];

    public string ValidationSummary => string.Join(Environment.NewLine, GetValidationMessages());

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

            if (promptForSequenceStart)
            {
                await DispatchAsync(ValidateForm);
            }

            if (promptForSequenceStart && HasErrors)
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

    public RetentionSettingsDraft BuildDraftForValidation()
    {
        return BuildDraft();
    }

    public IReadOnlyList<string> GetFluentValidationRuleNames(string propertyName)
    {
        return ValidationRuleNamesByProperty.TryGetValue(propertyName, out var ruleNames)
            ? ruleNames
            : [propertyName];
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

    private void ValidateForm()
    {
        ValidateAllProperties();
        ValidateProperty(TargetPaths, nameof(TargetPaths));
        OnPropertyChanged(nameof(ValidationSummary));
    }

    private void OnTargetPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ValidateProperty(TargetPaths, nameof(TargetPaths));
    }

    private void ValidateScheduleProperties()
    {
        ValidateProperty(ScheduleHours, nameof(ScheduleHours));
        ValidateProperty(ScheduleMinutes, nameof(ScheduleMinutes));
        ValidateProperty(ScheduleSeconds, nameof(ScheduleSeconds));
    }

    private void ValidateDeletionOptionProperties()
    {
        ValidateProperty(UseMaximumAge, nameof(UseMaximumAge));
        ValidateProperty(UseMinimumFileSize, nameof(UseMinimumFileSize));
        ValidateProperty(UseNamePatterns, nameof(UseNamePatterns));
    }

    private IEnumerable<string> GetValidationMessages()
    {
        return ValidatedPropertyNames
            .SelectMany(propertyName => GetErrors(propertyName).Cast<object>())
            .Select(error => error switch
            {
                ValidationResult validationResult => validationResult.ErrorMessage ?? string.Empty,
                string message => message,
                _ => error.ToString() ?? string.Empty
            })
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct();
    }

    partial void OnScheduleHoursChanged(int value)
    {
        ValidateScheduleProperties();
    }

    partial void OnScheduleMinutesChanged(int value)
    {
        ValidateScheduleProperties();
    }

    partial void OnScheduleSecondsChanged(int value)
    {
        ValidateScheduleProperties();
    }

    partial void OnUseMaximumAgeChanged(bool value)
    {
        ValidateProperty(MaximumAgeDays, nameof(MaximumAgeDays));
        ValidateDeletionOptionProperties();
    }

    partial void OnMaximumAgeDaysChanged(double? value)
    {
        ValidateProperty(MaximumAgeDays, nameof(MaximumAgeDays));
    }

    partial void OnUseMinimumFileSizeChanged(bool value)
    {
        ValidateProperty(MinimumFileSizeKb, nameof(MinimumFileSizeKb));
        ValidateDeletionOptionProperties();
    }

    partial void OnMinimumFileSizeKbChanged(double? value)
    {
        ValidateProperty(MinimumFileSizeKb, nameof(MinimumFileSizeKb));
    }

    partial void OnUseNamePatternsChanged(bool value)
    {
        ValidateProperty(NamePatternsText, nameof(NamePatternsText));
        ValidateDeletionOptionProperties();
    }

    partial void OnNamePatternsTextChanged(string value)
    {
        ValidateProperty(NamePatternsText, nameof(NamePatternsText));
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
