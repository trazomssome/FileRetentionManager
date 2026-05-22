using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Rules;

public sealed class CompositeRetentionRule : IRetentionRule
{
    private readonly LastWriteTimeRetentionRule lastWriteTimeRetentionRule = new();
    private readonly FileSizeRetentionRule fileSizeRetentionRule = new();
    private readonly FileNamePatternRetentionRule fileNamePatternRetentionRule = new();

    public CompositeRetentionRule()
    {
    }

    public CompositeRetentionRule(IEnumerable<IRetentionRule> rules)
        : this()
    {
    }

    public static CompositeRetentionRule Default { get; } = new();

    public bool IsMatch(FileMetadata file, RetentionCriteria criteria)
    {
        if (!criteria.HasAnyCondition)
        {
            return false;
        }

        var evaluations = new List<bool>();

        if (criteria.OlderThanUtc.HasValue)
        {
            evaluations.Add(lastWriteTimeRetentionRule.IsMatch(file, criteria));
        }

        if (criteria.MinimumSizeBytes.HasValue)
        {
            evaluations.Add(fileSizeRetentionRule.IsMatch(file, criteria));
        }

        if (criteria.NamePatterns.Count > 0)
        {
            evaluations.Add(fileNamePatternRetentionRule.IsMatch(file, criteria));
        }

        return criteria.ConditionMode == ConditionJoinMode.And
            ? evaluations.All(isMatch => isMatch)
            : evaluations.Any(isMatch => isMatch);
    }
}
