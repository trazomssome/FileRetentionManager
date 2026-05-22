using System.Globalization;
using System.Text;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileRetentionManager.Infrastructure.WPF.Services;

public sealed class MarkdownReportGenerator : IReportGenerator
{
    private readonly IFileSystemService fileSystemService;
    private readonly string reportDirectory;
    private readonly ILogger<MarkdownReportGenerator> logger;

    public MarkdownReportGenerator(
        IFileSystemService fileSystemService,
        string reportDirectory,
        ILogger<MarkdownReportGenerator>? logger = null)
    {
        this.fileSystemService = fileSystemService;
        this.reportDirectory = reportDirectory;
        this.logger = logger ?? NullLogger<MarkdownReportGenerator>.Instance;
    }

    public async Task<ReportArtifact> GenerateAsync(RetentionCycleReport report, CancellationToken cancellationToken)
    {
        fileSystemService.EnsureDirectory(reportDirectory);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"retention-report-{report.StartedAtUtc:yyyyMMdd-HHmmss}-{report.CycleId}.md");
        var reportPath = fileSystemService.CombinePath(reportDirectory, fileName);
        var content = BuildMarkdown(report);

        await fileSystemService.WriteAllTextAsync(reportPath, content, cancellationToken);
        logger.LogInformation("Generated retention report {ReportPath}", reportPath);

        return new ReportArtifact(reportPath, content);
    }

    private static string BuildMarkdown(RetentionCycleReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# File Retention Report");
        builder.AppendLine();
        builder.AppendLine($"- Cycle ID: `{report.CycleId}`");
        builder.AppendLine($"- Started (UTC): `{report.StartedAtUtc:O}`");
        builder.AppendLine($"- Completed (UTC): `{report.CompletedAtUtc:O}`");
        builder.AppendLine($"- Sequence start approved: `{report.WasSequenceStartApproved}`");
        builder.AppendLine($"- Sequence start prompted: `{report.WasSequenceStartPrompted}`");
        builder.AppendLine();
        builder.AppendLine("## Targets");
        builder.AppendLine();

        foreach (var targetPath in report.TargetPaths)
        {
            builder.AppendLine($"- `{targetPath}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Criteria");
        builder.AppendLine();
        builder.AppendLine($"- Condition mode: `{report.Criteria.ConditionMode}`");
        builder.AppendLine($"- Older than UTC: `{FormatNullableDate(report.Criteria.OlderThanUtc)}`");
        builder.AppendLine($"- Minimum size: `{FormatNullableBytes(report.Criteria.MinimumSizeBytes)}`");
        builder.AppendLine($"- Name patterns: `{FormatPatterns(report.Criteria.NamePatterns)}`");
        builder.AppendLine($"- Include subdirectories: `{report.Criteria.IncludeSubdirectories}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Matched files: `{report.Candidates.Count}`");
        builder.AppendLine($"- Deleted files: `{report.Results.Count(result => result.Succeeded)}`");
        builder.AppendLine($"- Failed files: `{report.Results.Count(result => !result.Succeeded)}`");
        builder.AppendLine();

        if (report.Notes.Count > 0)
        {
            builder.AppendLine("## Notes");
            builder.AppendLine();

            foreach (var note in report.Notes)
            {
                builder.AppendLine($"- {note}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Status | Path | Error |");
        builder.AppendLine("| --- | --- | --- |");

        if (report.Results.Count == 0)
        {
            builder.AppendLine("| None | No files were deleted. | |");
        }
        else
        {
            foreach (var result in report.Results)
            {
                var status = result.Succeeded ? "Deleted" : "Failed";
                builder.AppendLine($"| {status} | `{EscapePipe(result.Path)}` | {EscapePipe(result.ErrorMessage ?? string.Empty)} |");
            }
        }

        if (report.Candidates.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Matched Files");
            builder.AppendLine();
            builder.AppendLine("| Name | Size | Last modified (UTC) | Path |");
            builder.AppendLine("| --- | ---: | --- | --- |");

            foreach (var candidate in report.Candidates)
            {
                builder.AppendLine($"| {EscapePipe(candidate.Name)} | {FormatBytes(candidate.Length)} | {candidate.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} | `{EscapePipe(candidate.Path)}` |");
            }
        }

        return builder.ToString();
    }

    private static string FormatNullableDate(DateTimeOffset? value)
    {
        return value is null ? "Disabled" : value.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatNullableBytes(long? value)
    {
        return value is null ? "Disabled" : FormatBytes(value.Value);
    }

    private static string FormatPatterns(IReadOnlyList<string> patterns)
    {
        return patterns.Count == 0 ? "Disabled" : string.Join(", ", patterns);
    }

    private static string FormatBytes(long bytes)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{bytes:N0} bytes");
    }

    private static string EscapePipe(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
