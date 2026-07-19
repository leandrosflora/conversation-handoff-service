using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Domain;
using conversation_handoff_service.Platform;

namespace conversation_handoff_service.Adapters.Outbound.Persistence;

public class PostgresHandoffRequestRepository(
    NpgsqlDataSource dataSource,
    TenantContext tenantContext) : IHandoffRequestRepository
{
    private static readonly Guid SeedConversationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private const string TargetQueue = "human-support";
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    private const string InsertSql = """
        INSERT INTO conversation.handoffs
            (tenant_id, conversation_id, reason, target_queue, metadata, idempotency_key)
        VALUES
            (@tenant_id, @conversation_id, @reason, @target_queue, @metadata::jsonb, @idempotency_key)
        ON CONFLICT (tenant_id, idempotency_key) DO NOTHING;
        """;

    public async Task InsertAsync(
        HandoffRequestRecord request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(tenantContext.TenantId);
        var metadata = JsonSerializer.Serialize(new
        {
            tenantId = tenantContext.TenantId,
            externalConversationId = request.ConversationId
        });
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(InsertSql, connection);
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("conversation_id", SeedConversationId);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("target_queue", TargetQueue);
            command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb) { Value = metadata });
            command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw new HandoffRequestRepositoryUnavailableException("Failed to reach PostgreSQL.", ex);
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;
        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            const string sql = """
                ALTER TABLE conversation.handoffs ADD COLUMN IF NOT EXISTS tenant_id uuid;
                ALTER TABLE conversation.handoffs ADD COLUMN IF NOT EXISTS idempotency_key text;
                DROP INDEX IF EXISTS conversation.ux_handoffs_idempotency_key;
                CREATE UNIQUE INDEX IF NOT EXISTS ux_handoffs_tenant_idempotency_key
                    ON conversation.handoffs (tenant_id, idempotency_key)
                    WHERE idempotency_key IS NOT NULL;
                CREATE INDEX IF NOT EXISTS idx_handoffs_tenant_status_requested
                    ON conversation.handoffs (tenant_id, status, requested_at DESC);
                """;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
