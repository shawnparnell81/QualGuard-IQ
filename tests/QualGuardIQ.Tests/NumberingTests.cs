using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class NumberingTests(TestApp app)
{
    private async Task<string> Allocate(bool commit)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var n = await scope.ServiceProvider.GetRequiredService<INumberingService>().NextAsync("Action", null);
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        return n;
    }

    [Fact]
    public async Task Concurrent_allocations_are_unique()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Allocate(true)));
        Assert.Equal(20, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches("^ACT-\\d{4}$", n));
    }

    [Fact]
    public async Task Rolled_back_allocation_is_reused()
    {
        var a = await Allocate(false);
        var b = await Allocate(true);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Allocation_outside_a_transaction_is_refused()
    {
        using var scope = app.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<INumberingService>().NextAsync("Action", null));
    }

    [Fact]
    public async Task A_reserved_block_is_consecutive_and_its_unused_end_is_handed_back()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var block = await numbering.ReserveAsync("Action", null, 5);
        var taken = new List<string>();
        for (var i = 0; i < 3; i++) { Assert.True(block.TryTake(out var n)); taken.Add(n); }
        await numbering.ReturnUnusedAsync(block);
        var next = await numbering.NextAsync("Action", null);
        await tx.CommitAsync();
        var values = taken.Append(next).Select(n => int.Parse(n["ACT-".Length..])).ToList();
        Assert.Equal(Enumerable.Range(values[0], 4), values);
    }
}
