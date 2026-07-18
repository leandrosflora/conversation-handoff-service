using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using conversation_handoff_service.Adapters.Inbound.Http;
using conversation_handoff_service.Adapters.Outbound.Persistence;
using conversation_handoff_service.Application.Ports.Inbound;
using conversation_handoff_service.Application.Ports.Outbound;
using conversation_handoff_service.Application.UseCases;
using conversation_handoff_service.Configuration;
using conversation_handoff_service.Platform;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPlatform(builder.Configuration);
builder.Services.AddOptions<PostgresOptions>().Bind(builder.Configuration.GetSection(PostgresOptions.SectionName));
builder.Services.AddOptions<OtelOptions>().Bind(builder.Configuration.GetSection(OtelOptions.SectionName));

var otelEndpoint = builder.Configuration.GetSection(OtelOptions.SectionName).Get<OtelOptions>()?.OtlpEndpoint
    ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("conversation-handoff-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddNpgsql()
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint)));

builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var cs = new NpgsqlConnectionStringBuilder(options.ConnectionString) { Timeout = 5, CommandTimeout = 5 };
    return new NpgsqlDataSourceBuilder(cs.ConnectionString).Build();
});
builder.Services.AddScoped<IHandoffRequestRepository, PostgresHandoffRequestRepository>();
builder.Services.AddScoped<IRecordHandoffRequestUseCase, RecordHandoffRequestUseCase>();

builder.Logging.Configure(options => options.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UsePlatform();
app.MapGet("/health/ready", async (
    NpgsqlDataSource dataSource,
    IOptions<InternalAuthOptions> authOptions,
    CancellationToken cancellationToken) =>
{
    var failures = new List<string>();
    if (string.IsNullOrWhiteSpace(authOptions.Value.SigningKey)) failures.Add("internal_auth_signing_key_missing");
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
    catch { failures.Add("postgres_unavailable"); }
    return failures.Count == 0
        ? Results.Ok(new { status = "ready", failures })
        : Results.Json(new { status = "not_ready", failures }, statusCode: 503);
}).AllowAnonymous();
app.MapHandoffRequestEndpoints();
app.Run();

public partial class Program;
