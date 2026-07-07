using Microsoft.Extensions.Options;
using Yarpa.Api.Services;

namespace Yarpa.Api.Middleware;

/// <summary>
/// Rejects requests whose body exceeds <see cref="SecurityOptions.MaxRequestBodyBytes"/>
/// with HTTP 413. Enforced via the declared Content-Length (so it works behind the
/// test server as well as Kestrel) and by capping the server's own body-size feature.
/// Runs before authentication so oversized payloads are cheap to reject.
/// </summary>
public sealed class PayloadSizeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxBytes;

    public PayloadSizeMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
    {
        _next = next;
        _maxBytes = options.Value.MaxRequestBodyBytes > 0
            ? options.Value.MaxRequestBodyBytes
            : 5 * 1024 * 1024;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Tighten the server body-size limit for this request when it is adjustable.
        var sizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize = _maxBytes;

        long? contentLength = context.Request.ContentLength;
        if (contentLength.HasValue && contentLength.Value > _maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"גוף הבקשה גדול מדי (מקסימום {_maxBytes} בייטים)"
            });
            return;
        }

        await _next(context);
    }
}
