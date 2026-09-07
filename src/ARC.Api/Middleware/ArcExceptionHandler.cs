using Microsoft.AspNetCore.Diagnostics;
using ARC.Data.Exceptions;
using ARC.Domain.Exceptions;
using ARC.Tools.Exceptions;

namespace ARC.Api.Middleware;

public sealed class ArcExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, message) = exception switch
        {
            InvalidGateDecisionException ex => (StatusCodes.Status400BadRequest, ex.Message),
            UnauthorizedAccessException ex => (StatusCodes.Status403Forbidden, ex.Message),
            ToolException ex => (StatusCodes.Status400BadRequest, ex.Message),
            EntityNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            DataAccessException ex => (StatusCodes.Status503ServiceUnavailable, ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
            InvalidOperationException ex => (StatusCodes.Status409Conflict, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        return true;
    }
}
