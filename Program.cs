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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<PostgresOptions>()
    .Bind(builder.Configuration.GetSection(PostgresOptions.SectionName));
builder.Services.AddOptions<OtelOptions>()
    .Bind(builder.Configuration.GetSection(OtelOptions.SectionName));

// Exports the ASP.NET Core Activities (correlated in logs via TraceId/SpanId/ParentId below)
// to Jaeger via OTLP, matching the other .NET services in this workspace. AddNpgsql() gives
// span visibility into the one query this service ever runs, since datastore latency is its
// primary operational concern.
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
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
    {
        // Bounded so an unreachable PostgreSQL fails fast into a 503 instead of hanging on
        // Npgsql's much longer defaults (15s open / 30s command).
        Timeout = 5,
        CommandTimeout = 5
    };
    return new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString).Build();
});
builder.Services.AddScoped<IHandoffRequestRepository, PostgresHandoffRequestRepository>();
builder.Services.AddScoped<IRecordHandoffRequestUseCase, RecordHandoffRequestUseCase>();

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId
        | ActivityTrackingOptions.SpanId
        | ActivityTrackingOptions.ParentId;
});
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapHandoffRequestEndpoints();

app.Run();

public partial class Program;
