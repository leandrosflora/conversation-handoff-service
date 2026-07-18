using conversation_handoff_service.Application.Ports.Inbound;
using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Adapters.Inbound.Http;

public static class HandoffRequestEndpoints
{
    public static IEndpointRouteBuilder MapHandoffRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/handoffs", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HandoffRequestDto request,
        IRecordHandoffRequestUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            return Results.BadRequest(new { error = "conversationId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new { error = "reason is required." });
        }

        var handoffRequest = new HandoffRequestRecord
        {
            ConversationId = request.ConversationId,
            Reason = request.Reason
        };

        var result = await useCase.ExecuteAsync(handoffRequest, cancellationToken);

        return result switch
        {
            RecordHandoffRequestResult.Recorded => Results.Accepted(),
            RecordHandoffRequestResult.RepositoryUnavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

public record HandoffRequestDto(string? ConversationId, string? Reason);
