using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Rules;

public sealed class LastWriteTimeRetentionRule : IRetentionRule
{
    public bool IsMatch(FileMetadata file, RetentionCriteria criteria)
    {
        return criteria.OlderThanUtc is null || file.LastWriteTimeUtc <= criteria.OlderThanUtc.Value;
    }
}
