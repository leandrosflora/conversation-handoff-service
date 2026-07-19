using System.Text.Json;
using Npgsql;
using conversation_handoff_service.Adapters.Outbound.Persistence;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Domain;
using conversation_handoff_service.Platform;

namespace conversation_handoff_service.Tests.Persistence;

public class PostgresHandoffRequestRepositoryTests(PostgresHandoffRequestRepositoryFixture fixture)
    : IClassFixture<PostgresHandoffRequestRepositoryFixture>
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task InsertAsync_ValidRequest_WritesExpectedRow()
    {
        await using var dataSource = fixture.CreateDataSource();
        var tenantContext = new TenantContext();
        using var tenantScope = tenantContext.Push(TenantId);
        var repository = new PostgresHandoffRequestRepository(dataSource, tenantContext);

        await repository.InsertAsync(
            new HandoffRequestRecord
            {
                ConversationId = "5511999990000",
                Reason = "agent_runtime_unavailable"
            },
            "idem-handoff-1",
            CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT conversation_id, reason, target_queue, status, metadata
            FROM conversation.handoffs
            WHERE reason = @reason
            """,
            connection);
        command.Parameters.AddWithValue("reason", "agent_runtime_unavailable");

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(Guid.Parse("70000000-0000-0000-0000-000000000001"), reader.GetGuid(0));
        Assert.Equal("agent_runtime_unavailable", reader.GetString(1));
        Assert.Equal("human-support", reader.GetString(2));
        Assert.Equal("pending", reader.GetString(3));

        using var metadata = JsonDocument.Parse(reader.GetString(4));
        Assert.Equal("5511999990000", metadata.RootElement.GetProperty("externalConversationId").GetString());

        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task InsertAsync_RepositoryUnreachable_ThrowsRepositoryUnavailableException()
    {
        // A data source pointed at a port nothing listens on, with a short timeout, simulates
        // PostgreSQL being unreachable without needing to actually stop the shared container.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Port = 1,
            Timeout = 1
        };
        await using var unreachableDataSource = NpgsqlDataSource.Create(connectionStringBuilder.ConnectionString);
        var tenantContext = new TenantContext();
        using var tenantScope = tenantContext.Push(TenantId);
        var repository = new PostgresHandoffRequestRepository(unreachableDataSource, tenantContext);

        await Assert.ThrowsAsync<HandoffRequestRepositoryUnavailableException>(() => repository.InsertAsync(
            new HandoffRequestRecord
            {
                ConversationId = "5511999990001",
                Reason = "agent_recommended"
            },
            "idem-handoff-2",
            CancellationToken.None));
    }
}
