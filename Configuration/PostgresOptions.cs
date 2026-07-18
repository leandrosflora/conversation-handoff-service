namespace conversation_handoff_service.Configuration;

public class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string ConnectionString { get; set; } = string.Empty;
}
