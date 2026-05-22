namespace FileRetentionManager.Domain.Models;

public sealed record DeletionResult(
    string Path,
    bool Succeeded,
    string? ErrorMessage);
