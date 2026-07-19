using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace conversation_handoff_service.Platform;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";
    public string Issuer { get; init; } = "conversational-ai-platform";
    public string ServiceName { get; init; } = "conversation-handoff-service";
    public string SigningKey { get; init; } = string.Empty;
}

public sealed class TenantContext
{
    public const string ClaimType = "tenant_id";
    private static readonly AsyncLocal<string?> Current = new();
    public string TenantId => Current.Value ?? throw new InvalidOperationException("Tenant context unavailable.");

    public IDisposable Push(string tenantId)
    {
        if (!TryNormalize(tenantId, out var canonical))
        {
            throw new ArgumentException("Tenant ID must be a non-empty UUID.", nameof(tenantId));
        }
        var previous = Current.Value;
        Current.Value = canonical;
        return new Scope(() => Current.Value = previous);
    }

    public static bool TryNormalize(string? tenantId, out string canonical)
    {
        canonical = string.Empty;
        if (!Guid.TryParse(tenantId?.Trim(), out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        canonical = parsed.ToString("D");
        return true;
    }

    public static bool TryResolve(ClaimsPrincipal principal, string? headerTenant, out string tenantId)
    {
        tenantId = string.Empty;
        if (!TryNormalize(headerTenant, out var header)
            || !TryNormalize(principal.FindFirstValue(ClaimType), out var claim)
            || !string.Equals(header, claim, StringComparison.Ordinal))
        {
            return false;
        }
        tenantId = claim;
        return true;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            release();
        }
    }
}

public sealed class PlatformMetrics
{
    private readonly ConcurrentDictionary<string, long> _values = new();
    public void Increment(string name, params (string Name, string Value)[] labels) =>
        _values.AddOrUpdate(Key(name, labels), 1, (_, value) => value + 1);
    public string Render() => string.Join('\n', _values.OrderBy(item => item.Key).Select(item => $"{item.Key} {item.Value}")) + "\n";

    private static string Key(string name, params (string Name, string Value)[] labels)
    {
        if (labels.Length == 0) return name;
        var rendered = string.Join(",", labels.Select(label =>
            $"{Regex.Replace(label.Name, "[^a-zA-Z0-9_:]", "_")}=\"{label.Value.Replace("\"", "\\\"")}\""));
        return $"{name}{{{rendered}}}";
    }
}

public static class PlatformExtensions
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(InternalAuthOptions.SectionName).Get<InternalAuthOptions>() ?? new();
        services.Configure<InternalAuthOptions>(configuration.GetSection(InternalAuthOptions.SectionName));
        services.AddSingleton<TenantContext>();
        services.AddSingleton<PlatformMetrics>();
        var key = Encoding.UTF8.GetByteCount(auth.SigningKey) >= 32
            ? auth.SigningKey
            : "invalid-missing-internal-auth-signing-key-32-bytes";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = auth.Issuer,
                ValidateAudience = true,
                ValidAudience = auth.ServiceName,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Sub
            };
        });
        services.AddAuthorization(options =>
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(JwtRegisteredClaimNames.Sub)
                .RequireClaim(TenantContext.ClaimType)
                .Build());
        return services;
    }

    public static WebApplication UsePlatform(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/metrics", (PlatformMetrics metrics) => Results.Text(metrics.Render(), "text/plain; version=0.0.4")).AllowAnonymous();
        return app;
    }
}
