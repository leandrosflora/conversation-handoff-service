using conversation_handoff_service.Application.Ports.Inbound;
using conversation_handoff_service.Domain;
using conversation_handoff_service.Platform;

namespace conversation_handoff_service.Adapters.Inbound.Http;

public static class HandoffRequestEndpoints
{
    public static IEndpointRouteBuilder MapHandoffRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/handoffs", HandleAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HandoffRequestDto request,
        HttpContext httpContext,
        IRecordHandoffRequestUseCase useCase,
        TenantContext tenantContext,
        PlatformMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!TenantContext.TryResolve(
                httpContext.User,
                httpContext.Request.Headers["X-Tenant-Id"].ToString(),
                out var tenantId))
        {
            return Results.Json(
                new { error = "X-Tenant-Id must be a UUID and match the signed tenant_id claim." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        if (string.IsNullOrWhiteSpace(request.ConversationId))
            return Results.BadRequest(new { error = "conversationId is required." });
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "reason is required." });

        using var tenantScope = tenantContext.Push(tenantId);
        var result = await useCase.ExecuteAsync(
            new HandoffRequestRecord
            {
                ConversationId = request.ConversationId,
                Reason = request.Reason
            },
            idempotencyKey,
            cancellationToken);

        metrics.Increment("handoff_requests_total", ("result", result.ToString().ToLowerInvariant()));
        return result switch
        {
            RecordHandoffRequestResult.Recorded => Results.Accepted(),
            RecordHandoffRequestResult.RepositoryUnavailable => Results.StatusCode(503),
            _ => Results.StatusCode(500)
        };
    }
}

public record HandoffRequestDto(string? ConversationId, string? Reason);
