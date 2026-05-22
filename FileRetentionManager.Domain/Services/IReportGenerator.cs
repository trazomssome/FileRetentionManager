using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Services;

public interface IReportGenerator
{
    Task<ReportArtifact> GenerateAsync(RetentionCycleReport report, CancellationToken cancellationToken);
}
