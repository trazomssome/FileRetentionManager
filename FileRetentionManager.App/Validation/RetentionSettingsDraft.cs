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
    IReadOnlyList<string> NamePatterns,
    ConditionJoinMode ConditionMode)
{
    public TimeSpan ScheduleInterval => new(ScheduleHours, ScheduleMinutes, ScheduleSeconds);

    public bool HasAnyDeletionCondition =>
        UseMaximumAge ||
        UseMinimumFileSize ||
        UseNamePatterns;
}
