namespace FileRetentionManager.Domain.Models;

public sealed record RetentionCycleReport(
    string CycleId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    RetentionCriteria Criteria,
    IReadOnlyList<string> TargetPaths,
    bool WasSequenceStartApproved,
    bool WasSequenceStartPrompted,
    IReadOnlyList<FileMetadata> Candidates,
    IReadOnlyList<DeletionResult> Results,
    IReadOnlyList<string> Notes);
