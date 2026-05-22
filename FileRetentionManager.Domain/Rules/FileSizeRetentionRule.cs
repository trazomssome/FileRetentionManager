using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Rules;

public sealed class FileSizeRetentionRule : IRetentionRule
{
    public bool IsMatch(FileMetadata file, RetentionCriteria criteria)
    {
        return criteria.MinimumSizeBytes is null || file.Length >= criteria.MinimumSizeBytes.Value;
    }
}
