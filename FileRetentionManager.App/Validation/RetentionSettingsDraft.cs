using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.App.Validation;

public sealed record RetentionSettingsDraft(
    int ScheduleHours,
    int ScheduleMinutes,
    int ScheduleSeconds,
    IReadOnlyList<string> TargetPaths,
    bool IncludeSubdirectories,
    bool UseMaximumAge,
    double? MaximumAgeDays,
    bool UseMinimumFileSize,
    double? MinimumFileSizeKb,
    bool UseNamePatterns,
    string NamePatternsText,
    ConditionJoinMode ConditionMode)
{
    private static readonly char[] NamePatternSeparators = ['\r', '\n', ';', ','];

    public TimeSpan ScheduleInterval => new(ScheduleHours, ScheduleMinutes, ScheduleSeconds);

    public bool HasAnyDeletionCondition =>
        UseMaximumAge ||
        UseMinimumFileSize ||
        UseNamePatterns;

    public IReadOnlyList<string> NamePatterns =>
        NamePatternsText.Split(NamePatternSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
