namespace conversation_handoff_service.Domain;

public class HandoffRequestRecord
{
    public required string ConversationId { get; init; }
    public required string Reason { get; init; }
}
