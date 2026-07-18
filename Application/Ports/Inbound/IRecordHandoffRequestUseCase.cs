using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Application.Ports.Inbound;

public interface IRecordHandoffRequestUseCase
{
    Task<RecordHandoffRequestResult> ExecuteAsync(
        HandoffRequestRecord request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public enum RecordHandoffRequestResult
{
    Recorded,
    RepositoryUnavailable
}
