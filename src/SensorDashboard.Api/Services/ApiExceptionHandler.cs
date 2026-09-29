using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SensorDashboard.Api.Services;

/// <summary>
/// Turns every unhandled exception into a ProblemDetails response with a trace ID (and never a
/// stack trace), choosing a status the client can act on: 503 + Retry-After for a database
/// that's briefly unavailable, 504 for timeouts, 500 otherwise. Requests the client itself
/// aborted are not errors and aren't logged as such.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public const int RetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request {Path} aborted by the client", http.Request.Path);
            http.Response.StatusCode = 499; // client closed request (nginx convention); nobody is listening
            return true;
        }

        var (status, title) = Classify(exception);
        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            http.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
            logger.LogWarning(exception, "Database unavailable while handling {Method} {Path}", http.Request.Method, http.Request.Path);
        }
        else if (status == StatusCodes.Status504GatewayTimeout)
        {
            logger.LogWarning(exception, "Timed out handling {Method} {Path}", http.Request.Method, http.Request.Path);
        }
        else
        {
            logger.LogError(exception, "Unhandled error handling {Method} {Path}", http.Request.Method, http.Request.Path);
        }

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
        });
    }

    internal static (int Status, string Title) Classify(Exception exception) => exception switch
    {
        TimeoutException or SqlException { Number: -2 } =>
            (StatusCodes.Status504GatewayTimeout, "The request timed out."),
        SqlException sql when IsTransient(sql) =>
            (StatusCodes.Status503ServiceUnavailable, "The database is temporarily unavailable; retry shortly."),
        // EF wraps the provider error when its retry strategy gives up.
        RetryLimitExceededException or DbUpdateException { InnerException: SqlException } when Transient(exception) =>
            (StatusCodes.Status503ServiceUnavailable, "The database is temporarily unavailable; retry shortly."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    private static bool Transient(Exception exception) =>
        exception is RetryLimitExceededException || exception.InnerException is SqlException sql && IsTransient(sql);

    // SQL Server errors worth retrying: deadlock/lock timeouts, throttling and failover
    // (1205, 1222, 40501, 40613, 49918-49920, 4060, 40197, 10928/9), and connection-level
    // failures (server unreachable, reset or refused: 0, 2, 20, 53, 64, 121, 233, 258,
    // 10053/4/60), plus logins refused while a restarting server recovers databases (18456).
    private static readonly HashSet<int> TransientErrors =
    [
        0, 2, 20, 53, 64, 121, 233, 258, 1204, 1205, 1222, 4060, 4221, 10053, 10054, 10060, 10928, 10929,
        18456, 40143, 40197, 40501, 40540, 40613, 49918, 49919, 49920,
    ];

    internal static bool IsTransient(SqlException exception) => TransientErrors.Contains(exception.Number);
}

internal static class ProblemDetailsTracing
{
    /// <summary>Every ProblemDetails carries the trace ID to quote when reporting an issue.</summary>
    public static void AddTraceId(ProblemDetailsContext context) =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
}
