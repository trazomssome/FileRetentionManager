namespace FileRetentionManager.Domain.Models;

public sealed record ReportArtifact(
    string Path,
    string Content);
