using System.Diagnostics;
using Prismedia.Infrastructure.Diagnostics;

namespace Prismedia.Api.Diagnostics;

/// <summary>
/// Measures wall-clock and database time for API and OPDS requests. Emits a
/// <c>Server-Timing</c> response header (total app time, database time, and command count) and
/// logs one line per request — Information when the request exceeds the slow threshold
/// (<c>Prismedia:SlowRequestThresholdMs</c>, default 250), Debug otherwise.
/// </summary>
public sealed class RequestTimingMiddleware {
    #region Static Variables

    private const double DefaultSlowThresholdMs = 250;

    #endregion

    #region Variables

    private readonly RequestDelegate _next;

    private readonly ILogger<RequestTimingMiddleware> _logger;

    private readonly double _slowThresholdMs;

    #endregion

    #region Constructors

    /// <summary>Creates the middleware, reading the slow-request threshold from configuration or using the default.</summary>
    public RequestTimingMiddleware(
        RequestDelegate next,
        ILogger<RequestTimingMiddleware> logger,
        IConfiguration configuration) {
        _next = next;
        _logger = logger;
        _slowThresholdMs =
            double.TryParse(configuration["Prismedia:SlowRequestThresholdMs"], out var configured) &&
            configured > 0
                ? configured
                : DefaultSlowThresholdMs;
    }

    #endregion

    #region Actions - Pipeline

    /// <summary>Times API and OPDS requests, adding the Server-Timing header and logging the duration; other paths pass through.</summary>
    public async Task InvokeAsync(HttpContext context) {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/opds")) {
            await _next(context);
            return;
        }

        var db = DbCommandMetrics.Begin();
        var stopwatch = Stopwatch.StartNew();
        context.Response.OnStarting(() => {
            // Headers must be written before the body starts; the durations reported here cover
            // work up to first byte, which is what browser dev tools attribute to the server.
            context.Response.Headers["Server-Timing"] =
                $"app;dur={stopwatch.Elapsed.TotalMilliseconds:F1}, " +
                $"db;dur={db.TotalTime.TotalMilliseconds:F1};desc=\"{db.Commands} cmds\"";
            return Task.CompletedTask;
        });

        try {
            await _next(context);
        } finally {
            stopwatch.Stop();
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            var level = elapsedMs >= _slowThresholdMs ? LogLevel.Information : LogLevel.Debug;
            _logger.Log(
                level,
                "{Method} {Path} -> {StatusCode} in {ElapsedMs:F1}ms (db {DbMs:F1}ms / {DbCommands} cmds)",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsedMs,
                db.TotalTime.TotalMilliseconds,
                db.Commands);
        }
    }

    #endregion
}
