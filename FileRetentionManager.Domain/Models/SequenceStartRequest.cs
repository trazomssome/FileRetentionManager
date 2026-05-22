namespace FileRetentionManager.Domain.Models;

public sealed record SequenceStartRequest(
    IReadOnlyList<FileMetadata> Files,
    RetentionCriteria Criteria,
    IReadOnlyList<string> TargetPaths)
{
    public int FileCount => Files.Count;

    public long TotalBytes => Files.Sum(file => file.Length);
}
