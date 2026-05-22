using FileRetentionManager.App.Validation;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Rules;
using FileRetentionManager.Domain.Services;

namespace FileRetentionManager.App.Services;

public sealed class RetentionSequenceService : IRetentionSequenceService
{
    private readonly IFileSystemService fileSystemService;
    private readonly IUserDecisionService userDecisionService;
    private readonly IReportGenerator reportGenerator;
    private readonly IRetentionRule retentionRule;
    private readonly SemaphoreSlim executionLock = new(1, 1);

    public RetentionSequenceService(
        IFileSystemService fileSystemService,
        IUserDecisionService userDecisionService,
        IReportGenerator reportGenerator,
        IRetentionRule retentionRule)
    {
        this.fileSystemService = fileSystemService;
        this.userDecisionService = userDecisionService;
        this.reportGenerator = reportGenerator;
        this.retentionRule = retentionRule;
    }

    public async Task<RetentionSequenceResult> ExecuteAsync(
        RetentionSettingsDraft draft,
        bool promptForSequenceStart,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await executionLock.WaitAsync(TimeSpan.Zero, cancellationToken))
            {
                return RetentionSequenceResult.AlreadyRunning;
            }
        }
        catch (OperationCanceledException)
        {
            return RetentionSequenceResult.Cancelled;
        }

        try
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var notes = new List<string>();
            var criteria = BuildCriteria(draft, startedAtUtc);
            var candidates = await FindCandidatesAsync(draft.TargetPaths, criteria, notes, cancellationToken);

            if (promptForSequenceStart)
            {
                var request = new SequenceStartRequest(candidates, criteria, draft.TargetPaths);
                var decision = await userDecisionService.AskAsync(request, cancellationToken);

                if (decision != UserDecision.Approved)
                {
                    return new RetentionSequenceResult(
                        RetentionSequenceStatus.Rejected,
                        null,
                        null,
                        candidates,
                        [],
                        notes);
                }
            }

            var deletionResults = candidates.Count > 0
                ? await DeleteCandidatesAsync(candidates, cancellationToken)
                : [];

            if (candidates.Count == 0)
            {
                notes.Add("No files matched the active retention criteria.");
            }

            var completedAtUtc = DateTimeOffset.UtcNow;
            var report = new RetentionCycleReport(
                Guid.NewGuid().ToString("N")[..8],
                startedAtUtc,
                completedAtUtc,
                criteria,
                draft.TargetPaths,
                true,
                promptForSequenceStart,
                candidates,
                deletionResults,
                notes);

            var artifact = await reportGenerator.GenerateAsync(report, cancellationToken);

            return new RetentionSequenceResult(
                RetentionSequenceStatus.Completed,
                completedAtUtc,
                artifact,
                candidates,
                deletionResults,
                notes);
        }
        catch (OperationCanceledException)
        {
            return RetentionSequenceResult.Cancelled;
        }
        finally
        {
            executionLock.Release();
        }
    }

    private async Task<IReadOnlyList<FileMetadata>> FindCandidatesAsync(
        IReadOnlyList<string> targetPaths,
        RetentionCriteria criteria,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var candidates = new List<FileMetadata>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetPath in targetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!fileSystemService.DirectoryExists(targetPath) && !fileSystemService.FileExists(targetPath))
            {
                notes.Add($"Target path does not exist: `{targetPath}`.");
                continue;
            }

            try
            {
                var files = fileSystemService.EnumerateFilesAsync(
                    targetPath,
                    criteria.IncludeSubdirectories,
                    cancellationToken);

                await foreach (var file in files.WithCancellation(cancellationToken))
                {
                    if (retentionRule.IsMatch(file, criteria) && seenPaths.Add(file.Path))
                    {
                        candidates.Add(file);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                notes.Add($"Target path could not be scanned: `{targetPath}`. {exception.Message}");
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<DeletionResult>> DeleteCandidatesAsync(
        IReadOnlyList<FileMetadata> candidates,
        CancellationToken cancellationToken)
    {
        var results = new List<DeletionResult>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await fileSystemService.DeleteFileAsync(candidate.Path, cancellationToken);
                results.Add(new DeletionResult(candidate.Path, true, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new DeletionResult(candidate.Path, false, exception.Message));
            }
        }

        return results;
    }

    private static RetentionCriteria BuildCriteria(RetentionSettingsDraft draft, DateTimeOffset nowUtc)
    {
        var olderThanUtc = draft.UseMaximumAge && draft.MaximumAgeDays.HasValue
            ? nowUtc.AddDays(-draft.MaximumAgeDays.Value)
            : (DateTimeOffset?)null;
        var minimumSizeBytes = draft.UseMinimumFileSize && draft.MinimumFileSizeKb.HasValue
            ? (long)(draft.MinimumFileSizeKb.Value * 1024)
            : (long?)null;
        var namePatterns = draft.UseNamePatterns
            ? draft.NamePatterns
            : [];

        return new RetentionCriteria(
            olderThanUtc,
            minimumSizeBytes,
            namePatterns,
            draft.TargetPaths,
            draft.IncludeSubdirectories,
            draft.ConditionMode);
    }
}
