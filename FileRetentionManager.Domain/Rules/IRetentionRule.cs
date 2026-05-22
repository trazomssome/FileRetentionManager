using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Rules;

public interface IRetentionRule
{
    bool IsMatch(FileMetadata file, RetentionCriteria criteria);
}
