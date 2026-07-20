using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace conversation_handoff_service.Tests.Testing;

/// <summary>
/// Mirrors conversation-handoff-service's own Platform/PlatformServices.cs (no InternalAuth
/// Enabled toggle - DefaultPolicy always requires an authenticated user) so
/// WebApplicationFactory-based endpoint tests can mint a JWT that satisfies it, instead of
/// bypassing auth entirely. conversation-handoff-service only accepts calls from
/// conversation-orchestrator (see openspec/changes/per-service-internal-auth-secrets), so the
/// test secret is registered as that single inbound caller and tokens are signed/kid-tagged
/// as if issued by it.
/// </summary>
public static class TestAuth
{
    public const string InboundSecret = "test-only-conversation-orchestrator-inbound-secret-32b";
    public const string Issuer = "conversational-ai-platform";
    public const string Audience = "conversation-handoff-service";
    public const string CallerServiceName = "conversation-orchestrator";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";

    public static void ConfigureSigningKey(IWebHostBuilder builder) =>
        builder.UseSetting($"InternalAuth:InboundSecrets:{CallerServiceName}", InboundSecret);

    public static string IssueToken()
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InboundSecret)),
            SecurityAlgorithms.HmacSha256);
        var header = new JwtHeader(credentials);
        header["kid"] = CallerServiceName;
        var payload = new JwtPayload(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, CallerServiceName),
                new Claim("tenant_id", TenantId)
            ],
            notBefore: now,
            expires: now.AddMinutes(5),
            issuedAt: null);
        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
