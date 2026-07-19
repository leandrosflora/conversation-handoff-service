using DotNet.Testcontainers.Configurations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace conversation_handoff_service.Tests.Persistence;

/// <summary>
/// Spins up a real PostgreSQL container running the actual conversational-ai-postgres-init.sql
/// (not a hand-rolled test schema), so PostgresHandoffRequestRepository is verified against the
/// same conversation.handoffs table/FK/seed conversation the real docker-compose stack provisions.
/// </summary>
public class PostgresHandoffRequestRepositoryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("conversational_ai")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithBindMount(
            ResolveInitScriptPath(), "/docker-entrypoint-initdb.d/001-conversational-ai-postgres-init.sql", AccessMode.ReadOnly)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource CreateDataSource() => NpgsqlDataSource.Create(ConnectionString);

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string ResolveInitScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "conversational-ai-demo-arch", "database", "conversational-ai-postgres-init.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate conversational-ai-postgres-init.sql by walking up from the test assembly directory.");
    }
}
