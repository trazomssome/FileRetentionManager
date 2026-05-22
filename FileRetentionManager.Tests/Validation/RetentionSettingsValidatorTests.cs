using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FluentValidation;

namespace FileRetentionManager.Tests.Validation;

public sealed class RetentionSettingsValidatorTests
{
    private readonly RetentionSettingsValidator validator = new();

    [Fact]
    public void Validate_IncludesScheduleIntervalError_OnEachScheduleInputProperty()
    {
        var draft = CreateDraft(scheduleHours: 0, scheduleMinutes: 0, scheduleSeconds: 0);

        Assert.Contains(
            "Schedule interval must be greater than zero.",
            ValidateProperty(draft, nameof(RetentionSettingsDraft.ScheduleHours)));
        Assert.Contains(
            "Schedule interval must be greater than zero.",
            ValidateProperty(draft, nameof(RetentionSettingsDraft.ScheduleMinutes)));
        Assert.Contains(
            "Schedule interval must be greater than zero.",
            ValidateProperty(draft, nameof(RetentionSettingsDraft.ScheduleSeconds)));
    }

    [Fact]
    public void Validate_IncludesDeletionOptionGroupError_OnEachOptionInputProperty()
    {
        var draft = CreateDraft(
            useMaximumAge: false,
            useMinimumFileSize: false,
            useNamePatterns: false);

        Assert.Equal(
            ["At least one deletion option must be enabled."],
            ValidateProperty(draft, nameof(RetentionSettingsDraft.UseMaximumAge)));
        Assert.Equal(
            ["At least one deletion option must be enabled."],
            ValidateProperty(draft, nameof(RetentionSettingsDraft.UseMinimumFileSize)));
        Assert.Equal(
            ["At least one deletion option must be enabled."],
            ValidateProperty(draft, nameof(RetentionSettingsDraft.UseNamePatterns)));
    }

    [Fact]
    public void Validate_ReturnsTargetPathError_ForTargetPathsProperty()
    {
        var draft = CreateDraft(targetPaths: []);

        var messages = ValidateProperty(draft, nameof(RetentionSettingsDraft.TargetPaths));

        Assert.Equal(["At least one target path is required."], messages);
    }

    [Fact]
    public void Validate_ReturnsDependentValueError_WhenOptionIsEnabled()
    {
        var draft = CreateDraft(
            useMinimumFileSize: true,
            minimumFileSizeKb: 0);

        var messages = ValidateProperty(draft, nameof(RetentionSettingsDraft.MinimumFileSizeKb));

        Assert.Equal(["Minimum size must be greater than zero."], messages);
    }

    [Fact]
    public void Validate_ReturnsNamePatternError_ForNamePatternsTextProperty()
    {
        var draft = CreateDraft(
            useNamePatterns: true,
            namePatternsText: string.Empty);

        var messages = ValidateProperty(draft, nameof(RetentionSettingsDraft.NamePatternsText));

        Assert.Equal(["At least one name pattern is required when the option is enabled."], messages);
    }

    private IReadOnlyList<string> ValidateProperty(RetentionSettingsDraft draft, string propertyName)
    {
        return validator
            .Validate(draft, options => options.IncludeProperties(propertyName))
            .Errors
            .Select(error => error.ErrorMessage)
            .ToArray();
    }

    private static RetentionSettingsDraft CreateDraft(
        int scheduleHours = 0,
        int scheduleMinutes = 1,
        int scheduleSeconds = 0,
        IReadOnlyList<string>? targetPaths = null,
        bool useMaximumAge = true,
        double? maximumAgeDays = 30,
        bool useMinimumFileSize = false,
        double? minimumFileSizeKb = 1,
        bool useNamePatterns = true,
        string namePatternsText = "*.tmp")
    {
        return new RetentionSettingsDraft(
            scheduleHours,
            scheduleMinutes,
            scheduleSeconds,
            targetPaths ?? [@"C:\RetentionTarget"],
            true,
            useMaximumAge,
            maximumAgeDays,
            useMinimumFileSize,
            minimumFileSizeKb,
            useNamePatterns,
            namePatternsText,
            ConditionJoinMode.And);
    }
}
