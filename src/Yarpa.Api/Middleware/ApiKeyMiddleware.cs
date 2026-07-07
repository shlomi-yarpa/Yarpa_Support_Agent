using Microsoft.EntityFrameworkCore;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Middleware;

/// <summary>
/// Validates the <c>X-Api-Key</c> request header on every call, except the unauthenticated
/// operational probes (<c>/health</c>, <c>/health/ready</c>, <c>/metrics</c>) which expose no
/// customer data. A matching, active API key is required; on success the resolved
/// <see cref="CustomerEntity"/> is stored in <c>HttpContext.Items["Customer"]</c>
/// for downstream controllers.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, YarpaDbContext db)
    {
        // Skip authentication for the operational probes (liveness/readiness/metrics)
        if (context.Request.Path.StartsWithSegments("/health")
            || context.Request.Path.StartsWithSegments("/metrics"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.FirstOrDefault()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "חסר API Key בבקשה" });
            return;
        }

        string rawKey = headerValues.First()!;
        string keyHash = YarpaDbContext.ComputeKeyHash(rawKey);

        CustomerEntity? customer = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == keyHash && k.IsActive && k.RevokedAtUtc == null)
            .Select(k => k.Customer)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (customer == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API Key לא תקין או לא פעיל" });
            return;
        }

        context.Items["Customer"] = customer;
        await _next(context);
    }
}
