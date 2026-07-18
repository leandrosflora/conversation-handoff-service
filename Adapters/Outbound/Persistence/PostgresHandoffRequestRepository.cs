using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Adapters.Outbound.Persistence;

public class PostgresHandoffRequestRepository(NpgsqlDataSource dataSource) : IHandoffRequestRepository
{
    private static readonly Guid SeedConversationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private const string TargetQueue = "human-support";

    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    private const string InsertSql = """
        INSERT INTO conversation.handoffs
            (conversation_id, reason, target_queue, metadata, idempotency_key)
        VALUES
            (@conversation_id, @reason, @target_queue, @metadata::jsonb, @idempotency_key)
        ON CONFLICT (idempotency_key) DO NOTHING;
        """;

    public async Task InsertAsync(
        HandoffRequestRecord request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new { externalConversationId = request.ConversationId });

        try
        {
            await EnsureSchemaAsync(cancellationToken);

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(InsertSql, connection);

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
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            const string sql = """
                ALTER TABLE conversation.handoffs
                    ADD COLUMN IF NOT EXISTS idempotency_key text;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_handoffs_idempotency_key
                    ON conversation.handoffs (idempotency_key);
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
