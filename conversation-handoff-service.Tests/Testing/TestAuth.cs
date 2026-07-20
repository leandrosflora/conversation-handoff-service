using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace conversation_handoff_service.Tests.Testing;

/// <summary>
/// Mirrors conversation-handoff-service's own Platform/PlatformServices.cs (no InternalAuth
/// Enabled toggle - FallbackPolicy always requires an authenticated user) so
/// WebApplicationFactory-based endpoint tests can mint a JWT that satisfies it, instead of
/// bypassing auth entirely.
/// </summary>
public static class TestAuth
{
    public const string SigningKey = "test-only-internal-auth-signing-key-32-bytes-min";
    public const string Issuer = "conversational-ai-platform";
    public const string Audience = "conversation-handoff-service";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";

    public static void ConfigureSigningKey(IWebHostBuilder builder) =>
        builder.UseSetting("InternalAuth:SigningKey", SigningKey);

    public static string IssueToken()
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "test-caller"),
                new Claim("tenant_id", TenantId)
            ],
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
