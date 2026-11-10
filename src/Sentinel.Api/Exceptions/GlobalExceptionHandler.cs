
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Infrastructure.Near;
using Sentinel.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace Sentinel.Api.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            NearRpcException => StatusCodes.Status502BadGateway,
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            GatewayRejectedException e => e.Code == "SECURITY_BLOCK" || e.Code == "HUMAN_APPROVAL_REQUIRED" ? 422 : 409,
            UnauthorizedAccessException => 403,
            KeyNotFoundException => 404,
            ArgumentException => 400,
            DbUpdateException => 503,
            Npgsql.NpgsqlException => 503,
            _ => StatusCodes.Status500InternalServerError
        };

        _logger.LogError(
            exception,
            "Request failed. TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                502 => "Blockchain service unavailable",
                504 => "Upstream service timed out",
                _ => "An unexpected error occurred"
            },
            Detail = "The request could not be completed.",
            Instance = httpContext.Request.Path
        };

        problem.Extensions["traceId"] =
            httpContext.TraceIdentifier;
        if (exception is PaymentExpiryException expiry)
        {
            problem.Extensions["code"] = expiry.Code;
            problem.Detail = expiry.Message;
        }
        if (exception is GatewayRejectedException rejected)
        {
            problem.Extensions["code"] = rejected.Code;
            problem.Extensions["assessment"] = rejected.Assessment;
        }

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}
