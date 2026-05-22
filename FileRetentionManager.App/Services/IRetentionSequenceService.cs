using FileRetentionManager.App.Validation;

namespace FileRetentionManager.App.Services;

public interface IRetentionSequenceService
{
    Task<RetentionSequenceResult> ExecuteAsync(
        RetentionSettingsDraft draft,
        bool promptForSequenceStart,
        CancellationToken cancellationToken);
}
