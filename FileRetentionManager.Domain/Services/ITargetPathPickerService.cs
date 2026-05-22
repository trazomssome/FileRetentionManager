namespace FileRetentionManager.Domain.Services;

public interface ITargetPathPickerService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken);
}
