using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Application.Ports.Outbound;

public interface IHandoffRequestRepository
{
    /// <summary>Throws <see cref="HandoffRequestRepositoryUnavailableException"/> if PostgreSQL cannot be reached.</summary>
    Task InsertAsync(
        HandoffRequestRecord request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public class HandoffRequestRepositoryUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
