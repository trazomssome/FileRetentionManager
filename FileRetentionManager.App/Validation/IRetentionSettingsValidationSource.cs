namespace FileRetentionManager.App.Validation;

public interface IRetentionSettingsValidationSource
{
    IReadOnlyList<string> ValidateRetentionSettingsProperty(string propertyName);
}
