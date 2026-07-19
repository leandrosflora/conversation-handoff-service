using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using conversation_handoff_service.Application.Ports.Inbound;
using conversation_handoff_service.Domain;
using conversation_handoff_service.Tests.Testing;

namespace conversation_handoff_service.Tests.Endpoints;

public class HandoffRequestEndpointsTests
{
    [Fact]
    public async Task PostHandoffs_ValidRequest_ReturnsAccepted()
    {
        var client = BuildClient(new StubUseCase(RecordHandoffRequestResult.Recorded));

        var response = await client.PostAsJsonAsync("/handoffs", new
        {
            conversationId = "5511999990000",
            reason = "agent_runtime_unavailable"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoffs_MissingConversationId_ReturnsBadRequest()
    {
        var client = BuildClient(new StubUseCase(RecordHandoffRequestResult.Recorded));

        var response = await client.PostAsJsonAsync("/handoffs", new
        {
            reason = "agent_runtime_unavailable"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoffs_MissingReason_ReturnsBadRequest()
    {
        var client = BuildClient(new StubUseCase(RecordHandoffRequestResult.Recorded));

        var response = await client.PostAsJsonAsync("/handoffs", new
        {
            conversationId = "5511999990000"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoffs_RepositoryUnavailable_ReturnsServiceUnavailable()
    {
        var client = BuildClient(new StubUseCase(RecordHandoffRequestResult.RepositoryUnavailable));

        var response = await client.PostAsJsonAsync("/handoffs", new
        {
            conversationId = "5511999990000",
            reason = "agent_runtime_unavailable"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient BuildClient(IRecordHandoffRequestUseCase useCase)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureSigningKey(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecordHandoffRequestUseCase>();
                services.AddScoped(_ => useCase);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-test-1");
        return client;
    }

    private class StubUseCase(RecordHandoffRequestResult result) : IRecordHandoffRequestUseCase
    {
        public Task<RecordHandoffRequestResult> ExecuteAsync(
            HandoffRequestRecord request, string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }
}
