namespace FileRetentionManager.Domain.Models;

public sealed record FileMetadata(
    string Path,
    string Name,
    long Length,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastWriteTimeUtc);
