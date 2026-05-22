using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileRetentionManager.App.Services;
using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using FluentValidation;

namespace FileRetentionManager.App.ViewModels;

public partial class MainViewModel : ObservableValidator, IRetentionSettingsValidationSource, IAsyncDisposable
{
    private static readonly Lazy<IReadOnlyList<string>> ValidatedPropertyNames = new(GetValidatedPropertyNames);

    private readonly IRetentionSequenceService retentionSequenceService;
    private readonly IRetentionScheduleService retentionScheduleService;
    private readonly ITargetPathPickerService targetPathPickerService;
    private readonly SynchronizationContext? synchronizationContext;

    private CancellationTokenSource? scheduleCancellation;
    private Task? scheduleTask;

    public MainViewModel(
        IRetentionSequenceService retentionSequenceService,
        IRetentionScheduleService retentionScheduleService,
        ITargetPathPickerService targetPathPickerService)
    {
        this.retentionSequenceService = retentionSequenceService;
        this.retentionScheduleService = retentionScheduleService;
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
            await StopScheduleAsync();
            IsRetentionEnabled = false;
            ActivationButtonText = "Enable";
            StatusMessage = "Retention is disabled.";
            return;
        }

        var draft = BuildDraft();
        var sequenceStarted = await ExecuteCycleFromViewModelAsync(
            draft,
            CancellationToken.None,
            promptForSequenceStart: true);

        if (!sequenceStarted)
        {
            return;
        }

        IsRetentionEnabled = true;
        ActivationButtonText = "Disable";
        StatusMessage = $"Retention is enabled. {StatusMessage}";
        StartSchedule(draft);
    }

    [RelayCommand]
    private async Task ExecuteNowAsync()
    {
        await ExecuteCycleFromViewModelAsync(BuildDraft(), CancellationToken.None, promptForSequenceStart: true);
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
            await retentionScheduleService.RunAsync(
                draft.ScheduleInterval,
                token => ExecuteCycleFromViewModelAsync(draft, token, promptForSequenceStart: false),
                cancellationToken);
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

    private async Task<bool> ExecuteCycleFromViewModelAsync(
        RetentionSettingsDraft draft,
        CancellationToken cancellationToken,
        bool promptForSequenceStart)
    {
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

            var result = await retentionSequenceService.ExecuteAsync(draft, promptForSequenceStart, cancellationToken);
            var sequenceStarted = false;
            await DispatchAsync(() => sequenceStarted = ApplySequenceResult(result));
            return sequenceStarted;
        }
        finally
        {
            await DispatchAsync(() => IsBusy = false);
        }
    }

    private bool ApplySequenceResult(RetentionSequenceResult result)
    {
        switch (result.Status)
        {
            case RetentionSequenceStatus.Completed:
                if (result.CompletedAtUtc is null || result.Report is null)
                {
                    throw new InvalidOperationException("Completed retention sequence results must include completion time and report artifact.");
                }

                ApplyCycleResult(
                    result.CompletedAtUtc.Value,
                    result.Report,
                    result.Candidates,
                    result.DeletionResults);
                return true;
            case RetentionSequenceStatus.Rejected:
                ApplySequenceStartRejected(result.Candidates);
                return false;
            case RetentionSequenceStatus.AlreadyRunning:
                StatusMessage = "A retention cycle is already running.";
                return false;
            case RetentionSequenceStatus.Cancelled:
                StatusMessage = "Retention cycle cancelled.";
                return false;
            default:
                throw new InvalidOperationException($"Unsupported retention sequence status: {result.Status}");
        }
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

    public IReadOnlyList<string> ValidateRetentionSettingsProperty(string propertyName)
    {
        var validationResult = Validator.Validate(
            BuildDraft(),
            options => options.IncludeProperties(propertyName));

        return validationResult.Errors
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToArray();
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
            NamePatternsText,
            ConditionMode);
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
        return ValidatedPropertyNames.Value
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

    private static IReadOnlyList<string> GetValidatedPropertyNames()
    {
        return typeof(MainViewModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetCustomAttribute<FluentValidationPropertyAttribute>() is not null)
            .Select(property => property.Name)
            .ToArray();
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

    private void StartSchedule(RetentionSettingsDraft draft)
    {
        scheduleCancellation = new CancellationTokenSource();
        scheduleTask = RunScheduleAsync(draft, scheduleCancellation.Token);
    }

    private async Task StopScheduleAsync()
    {
        var cancellation = scheduleCancellation;
        var runningTask = scheduleTask;

        scheduleCancellation = null;
        scheduleTask = null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            await cancellation.CancelAsync();

            if (runningTask is not null)
            {
                await runningTask;
            }
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopScheduleAsync();
    }
}
