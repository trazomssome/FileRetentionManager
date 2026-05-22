namespace FileRetentionManager.App.ViewModels;

public sealed record DeletionResultViewModel(
    string Path,
    string Status,
    string Detail);
