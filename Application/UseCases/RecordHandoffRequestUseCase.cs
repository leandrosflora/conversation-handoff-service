using conversation_handoff_service.Application.Ports.Inbound;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Application.UseCases;

public class RecordHandoffRequestUseCase(
    IHandoffRequestRepository repository,
    ILogger<RecordHandoffRequestUseCase> logger) : IRecordHandoffRequestUseCase
{
    public async Task<RecordHandoffRequestResult> ExecuteAsync(
        HandoffRequestRecord request, CancellationToken cancellationToken)
    {
        try
        {
            await repository.InsertAsync(request, cancellationToken);
            return RecordHandoffRequestResult.Recorded;
        }
        catch (HandoffRequestRepositoryUnavailableException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist handoff request for conversation {ConversationId}: repository unavailable",
                request.ConversationId);
            return RecordHandoffRequestResult.RepositoryUnavailable;
        }
    }
}
