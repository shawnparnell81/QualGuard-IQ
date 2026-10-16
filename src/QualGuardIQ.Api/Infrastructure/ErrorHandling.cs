using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>Maps known failures to problem details; unexpected errors never leak internals.</summary>
public sealed class ErrorHandler(ILogger<ErrorHandler> log, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, errors) = ex switch
        {
            ValidationException v => (400, "Validation failed", v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            AppException a => (a.StatusCode, a.Message, null),
            DbUpdateConcurrencyException => (409, "This record was changed by someone else. Reload and try again.", null),
            _ => (500, "An unexpected error occurred.", (Dictionary<string, string[]>?)null)
        };
        if (status == 500) log.LogError(ex, "Unhandled error");
        http.Response.StatusCode = status;
        var pd = new ProblemDetails { Status = status, Title = title };
        pd.Extensions["traceId"] = http.TraceIdentifier;
        if (errors is not null) pd.Extensions["errors"] = errors;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd, Exception = ex });
    }
}
