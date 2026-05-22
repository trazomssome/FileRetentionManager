namespace FileRetentionManager.Domain.Models;

public sealed record RetentionCriteria
{
    public RetentionCriteria(
        DateTimeOffset? olderThanUtc,
        long? minimumSizeBytes,
        IReadOnlyList<string> namePatterns)
        : this(
            olderThanUtc,
            minimumSizeBytes,
            namePatterns,
            [],
            true,
            ConditionJoinMode.And)
    {
    }

    public RetentionCriteria(
        DateTimeOffset? olderThanUtc,
        long? minimumSizeBytes,
        IReadOnlyList<string> namePatterns,
        IReadOnlyList<string> targetPaths,
        bool includeSubdirectories,
        ConditionJoinMode conditionMode)
    {
        OlderThanUtc = olderThanUtc;
        MinimumSizeBytes = minimumSizeBytes;
        NamePatterns = namePatterns;
        TargetPaths = targetPaths;
        IncludeSubdirectories = includeSubdirectories;
        ConditionMode = conditionMode;
    }

    public DateTimeOffset? OlderThanUtc { get; }

    public long? MinimumSizeBytes { get; }

    public IReadOnlyList<string> NamePatterns { get; }

    public IReadOnlyList<string> TargetPaths { get; }

    public bool IncludeSubdirectories { get; }

    public ConditionJoinMode ConditionMode { get; }

    public bool HasAnyCondition =>
        OlderThanUtc.HasValue ||
        MinimumSizeBytes.HasValue ||
        NamePatterns.Count > 0;
}
