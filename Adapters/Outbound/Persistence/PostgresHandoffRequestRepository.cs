using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Domain;

namespace conversation_handoff_service.Adapters.Outbound.Persistence;

public class PostgresHandoffRequestRepository(NpgsqlDataSource dataSource) : IHandoffRequestRepository
{
    // conversation.handoffs.conversation_id is a required FK into conversation.conversations, a
    // table no service in this workspace populates for real conversations (see design.md
    // Decision 2). Every handoff points at the pre-existing seed conversation row instead, and
    // the real conversation ID is preserved in metadata so it isn't lost.
    private static readonly Guid SeedConversationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private const string TargetQueue = "human-support";

    private const string InsertSql = """
        INSERT INTO conversation.handoffs
            (conversation_id, reason, target_queue, metadata)
        VALUES
            (@conversation_id, @reason, @target_queue, @metadata::jsonb)
        """;

    public async Task InsertAsync(HandoffRequestRecord request, CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new { externalConversationId = request.ConversationId });

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(InsertSql, connection);

            command.Parameters.AddWithValue("conversation_id", SeedConversationId);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("target_queue", TargetQueue);
            command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb) { Value = metadata });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw new HandoffRequestRepositoryUnavailableException("Failed to reach PostgreSQL.", ex);
        }
    }
}
