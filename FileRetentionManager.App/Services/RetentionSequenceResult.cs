using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.App.Services;

public sealed record RetentionSequenceResult(
    RetentionSequenceStatus Status,
    DateTimeOffset? CompletedAtUtc,
    ReportArtifact? Report,
    IReadOnlyList<FileMetadata> Candidates,
    IReadOnlyList<DeletionResult> DeletionResults,
    IReadOnlyList<string> Notes)
{
    public static RetentionSequenceResult AlreadyRunning { get; } = new(
        RetentionSequenceStatus.AlreadyRunning,
        null,
        null,
        [],
        [],
        []);

    public static RetentionSequenceResult Cancelled { get; } = new(
        RetentionSequenceStatus.Cancelled,
        null,
        null,
        [],
        [],
        []);
}
