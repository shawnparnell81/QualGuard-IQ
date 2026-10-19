using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QualGuardIQ.Api.Infrastructure;

namespace QualGuardIQ.Tests;

/// <summary>A request the browser cancels (for example a pack install the user walked away from) is logged as 499, not as a server error.</summary>
public sealed class ClientCancelTests
{
    sealed class NoProblems : IProblemDetailsService
    {
        public bool Wrote { get; private set; }
        public ValueTask WriteAsync(ProblemDetailsContext context) { Wrote = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task A_cancelled_request_gets_499_and_no_error_body()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        var problems = new NoProblems();
        var handled = await new ErrorHandler(NullLogger<ErrorHandler>.Instance, problems).TryHandleAsync(http, new OperationCanceledException(cts.Token), CancellationToken.None);
        Assert.True(handled);
        Assert.Equal(499, http.Response.StatusCode);
        Assert.False(problems.Wrote);
    }

    [Fact]
    public async Task A_cancellation_the_client_did_not_cause_is_still_a_server_error()
    {
        var http = new DefaultHttpContext();
        var problems = new NoProblems();
        await new ErrorHandler(NullLogger<ErrorHandler>.Instance, problems).TryHandleAsync(http, new TaskCanceledException("database timeout"), CancellationToken.None);
        Assert.Equal(500, http.Response.StatusCode);
        Assert.True(problems.Wrote);
    }
}
