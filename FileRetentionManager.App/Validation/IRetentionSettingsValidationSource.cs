using FluentValidation;

namespace FileRetentionManager.App.Validation;

public interface IRetentionSettingsValidationSource
{
    IValidator<RetentionSettingsDraft> Validator { get; }

    RetentionSettingsDraft BuildDraftForValidation();

    IReadOnlyList<string> GetFluentValidationRuleNames(string propertyName);
}
