using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NimbusFlow.Application.Jobs;

namespace NimbusFlow.Api.Infrastructure;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            WorkflowValidationException => (StatusCodes.Status400BadRequest, "The request is invalid."),
            JobNotFoundException => (StatusCodes.Status404NotFound, "The job was not found."),
            JobConflictException or DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "The requested state transition is not allowed."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled API exception");
        }
        else
        {
            logger.LogInformation(exception, "API request rejected with status {StatusCode}", status);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= 500 ? "Use the trace id when contacting support." : exception.Message,
                Type = $"https://httpstatuses.com/{status}",
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
