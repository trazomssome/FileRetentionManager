using FluentValidation;

namespace FileRetentionManager.App.Validation;

public sealed class RetentionSettingsValidator : AbstractValidator<RetentionSettingsDraft>
{
    public RetentionSettingsValidator()
    {
        RuleFor(settings => settings.ScheduleHours)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(24 * 30)
            .Must(HasPositiveScheduleInterval)
            .WithMessage("Schedule interval must be greater than zero.");

        RuleFor(settings => settings.ScheduleMinutes)
            .InclusiveBetween(0, 59)
            .Must(HasPositiveScheduleInterval)
            .WithMessage("Schedule interval must be greater than zero.");

        RuleFor(settings => settings.ScheduleSeconds)
            .InclusiveBetween(0, 59)
            .Must(HasPositiveScheduleInterval)
            .WithMessage("Schedule interval must be greater than zero.");

        RuleFor(settings => settings.TargetPaths)
            .Must(paths => paths.Count > 0)
            .WithMessage("At least one target path is required.");

        RuleFor(settings => settings.UseMaximumAge)
            .Must(HasAnyDeletionCondition)
            .WithMessage("At least one deletion option must be enabled.");

        RuleFor(settings => settings.UseMinimumFileSize)
            .Must(HasAnyDeletionCondition)
            .WithMessage("At least one deletion option must be enabled.");

        RuleFor(settings => settings.UseNamePatterns)
            .Must(HasAnyDeletionCondition)
            .WithMessage("At least one deletion option must be enabled.");

        RuleFor(settings => settings.MaximumAgeDays)
            .NotNull()
            .When(settings => settings.UseMaximumAge)
            .WithMessage("Maximum age is required when the option is enabled.");

        RuleFor(settings => settings.MaximumAgeDays)
            .GreaterThan(0)
            .When(settings => settings.UseMaximumAge && settings.MaximumAgeDays.HasValue)
            .WithMessage("Maximum age must be greater than zero.");

        RuleFor(settings => settings.MinimumFileSizeKb)
            .NotNull()
            .When(settings => settings.UseMinimumFileSize)
            .WithMessage("Minimum size is required when the option is enabled.");

        RuleFor(settings => settings.MinimumFileSizeKb)
            .GreaterThan(0)
            .When(settings => settings.UseMinimumFileSize && settings.MinimumFileSizeKb.HasValue)
            .WithMessage("Minimum size must be greater than zero.");

        RuleFor(settings => settings.NamePatternsText)
            .Must((settings, _) => settings.NamePatterns.Count > 0)
            .When(settings => settings.UseNamePatterns)
            .WithMessage("At least one name pattern is required when the option is enabled.");
    }

    private static bool HasPositiveScheduleInterval(RetentionSettingsDraft settings, int _)
    {
        return settings.ScheduleInterval > TimeSpan.Zero;
    }

    private static bool HasAnyDeletionCondition(RetentionSettingsDraft settings, bool _)
    {
        return settings.HasAnyDeletionCondition;
    }
}
