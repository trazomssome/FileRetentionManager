using System.ComponentModel.DataAnnotations;

namespace FileRetentionManager.App.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FluentValidationPropertyAttribute : ValidationAttribute
{
    public override bool RequiresValidationContext => true;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (validationContext.ObjectInstance is not IRetentionSettingsValidationSource source ||
            string.IsNullOrWhiteSpace(validationContext.MemberName))
        {
            return ValidationResult.Success;
        }

        var messages = source
            .ValidateRetentionSettingsProperty(validationContext.MemberName)
            .ToArray();

        return messages.Length == 0
            ? ValidationResult.Success
            : new ValidationResult(string.Join(Environment.NewLine, messages), [validationContext.MemberName]);
    }
}
