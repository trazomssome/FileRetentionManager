using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Services;

public interface IUserDecisionService
{
    Task<UserDecision> AskAsync(SequenceStartRequest request, CancellationToken cancellationToken);
}
