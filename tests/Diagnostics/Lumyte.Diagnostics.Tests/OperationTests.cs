using System.Diagnostics;
using Lumyte.Diagnostics.Sample;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.Tests;

/// <summary>Tests real generated operations, scoped sharing, validation and lifecycle.</summary>
public sealed class OperationTests
{
    /// <summary>Checks DI dispatch and same-request deduplication before and after execution.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task ScopedGeneratedInputExecutesOnceAsync()
    {
        using ServiceProvider services = Build();
        using IServiceScope game = services.CreateScope();
        InputOverrideService input = game.ServiceProvider.GetRequiredService<InputOverrideService>();
        IDiagnosticPump<BeforeInputProcessing> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
        Assert.Empty(pump.Catalog);
        pump.Activate();
        Assert.Equal("input", Assert.Single(pump.Catalog).Subsystem.Id);
        Assert.Equal("override-button", Assert.Single(pump.Catalog[0].Operations).Id);
        DiagnosticRequest request = Request();
        Task<DiagnosticOperationResult> first = pump.SubmitAsync(request);
        Assert.Same(first, pump.SubmitAsync(request));
        Assert.False(input.Read("Jump", false));
        pump.Pump(new(17, Stopwatch.GetTimestamp()), new(TimeSpan.FromSeconds(1), 8));
        Assert.True(input.Read("Jump", false));
        DiagnosticOperationResult result = await first;
        Assert.Equal("success", result.Status);
        Assert.True(Guid.TryParse(result.Values!["lease-id"].String, out _));
        Assert.Same(first, pump.SubmitAsync(request));
        input.ReleaseSession(request.Context.SessionId);
        Assert.False(input.Read("Jump", false));
        pump.Deactivate();
    }

    /// <summary>Checks schemas, access, revisions, expiry and domain constraints.</summary>
    [Fact]
    public void InvalidRequestsNeverMutateInput()
    {
        var input = new InputOverrideService(TimeProvider.System);
        var set = new DiagnosticOperationSet((IDiagnosticContributor)new InputDiagnostics(input));
        DiagnosticRequest request = Request();
        Assert.Equal("override-button", Assert.Single(set.Catalog).Id);
        Assert.Equal(["button", "pressed", "duration-ms"], set.Catalog[0].Arguments.Select(field => field.Id));
        Assert.Equal("forbidden", set.Invoke(request.OperationId, request.Arguments, request.Context, new HashSet<DiagnosticPermission>()).Code);
        var bad = new Dictionary<string, DiagnosticValue>(request.Arguments) { ["duration-ms"] = DiagnosticValue.From("200") };
        Assert.Equal("invalid-arguments", set.Invoke(request.OperationId, bad, request.Context, request.Permissions).Code);
        bad["duration-ms"] = DiagnosticValue.From(6000L);
        Assert.Equal("invalid-input", set.Invoke(request.OperationId, bad, request.Context, request.Permissions).Code);
        Assert.False(input.Read("Jump", false));
    }

    /// <summary>Checks expiry and cancellation before execution, scope isolation and pending-request rejection.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task QueueEnforcesLifetimeAndScopeAsync()
    {
        using ServiceProvider services = Build();
        using IServiceScope game = services.CreateScope();
        using IServiceScope second = services.CreateScope();
        IDiagnosticPump<BeforeInputProcessing> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
        pump.Activate();
        Assert.NotSame(game.ServiceProvider.GetRequiredService<InputOverrideService>(), second.ServiceProvider.GetRequiredService<InputOverrideService>());
        Assert.Equal("expired", (await pump.SubmitAsync(Request() with { DeadlineTimestamp = 0 })).Code);
        using var cancellation = new CancellationTokenSource();
        DiagnosticRequest request = Request();
        Task<DiagnosticOperationResult> pending = pump.SubmitAsync(request with { Context = request.Context with { CancellationToken = cancellation.Token } });
        cancellation.Cancel();
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 8));
        Assert.Equal("cancelled", (await pending).Code);
        pending = pump.SubmitAsync(Request());
        pump.Deactivate();
        Assert.Equal("target-gone", (await pending).Code);
    }

    /// <summary>Checks owner-thread execution and contributor lifetime validation.</summary>
    [Fact]
    public void OwnershipAndLifetimeAreExplicit()
    {
        using ServiceProvider services = Build();
        using IServiceScope game = services.CreateScope();
        IDiagnosticPump<BeforeInputProcessing> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
        pump.Activate();
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() => pump.Pump(default, new(TimeSpan.FromSeconds(1), 1))));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        var wrong = new ServiceCollection();
        wrong.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input");
        wrong.AddSingleton<InputDiagnostics>();
        Assert.Throws<ArgumentException>(() => wrong.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1)));
        pump.Deactivate();
    }

    /// <summary>Checks the input lease expires before a resumed input update.</summary>
    [Fact]
    public void LeaseExpiresOnMonotonicClock()
    {
        var clock = new ManualClock();
        var input = new InputOverrideService(clock);
        DiagnosticOperationContext context = Request().Context;
        Assert.True(input.Override(context, "Jump", true, 200).IsSuccess);
        Assert.True(input.Read("Jump", false));
        clock.Timestamp = 201;
        Assert.False(input.Read("Jump", false));
    }

    /// <summary>Checks queued requests own their payload and trusted permissions.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task QueuedPayloadIsDetachedAsync()
    {
        using ServiceProvider services = Build();
        using IServiceScope game = services.CreateScope();
        IDiagnosticPump<BeforeInputProcessing> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
        InputOverrideService input = game.ServiceProvider.GetRequiredService<InputOverrideService>();
        pump.Activate();
        DiagnosticRequest request = Request();
        Task<DiagnosticOperationResult> pending = pump.SubmitAsync(request);
        ((Dictionary<string, DiagnosticValue>)request.Arguments)["pressed"] = DiagnosticValue.From(false);
        ((HashSet<DiagnosticPermission>)request.Permissions).Clear();
        Assert.Equal("request-id-conflict", (await pump.SubmitAsync(request)).Code);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 8));
        Assert.Equal("success", (await pending).Status);
        Assert.True(input.Read("Jump", false));
        input.ReleaseSession(request.Context.SessionId);
        pump.Deactivate();
        Assert.Empty(pump.Catalog);
    }

    /// <summary>Checks hand-written schemas cannot cause numeric overflow during validation.</summary>
    [Fact]
    public void InvalidIntegerBoundsAreRejectedAtRegistration()
    {
        var builder = new DiagnosticBuilder();
        Assert.Throws<ArgumentException>(() => builder.Operation(
            new("bad", "Bad", DiagnosticPermission.Observe, [new("value", DiagnosticValueKind.Int64, Minimum: 1e100)], []),
            (_, _) => DiagnosticOperationResult.Reject("unused", "Unused")));
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<InputOverrideService>();
        services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input");
        services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static DiagnosticRequest Request() => new(
        "input",
        "override-button",
        new(Guid.NewGuid(), Guid.NewGuid(), default, "developer", null, default),
        new Dictionary<string, DiagnosticValue>
        {
            ["button"] = DiagnosticValue.From("Jump"),
            ["pressed"] = DiagnosticValue.From(true),
            ["duration-ms"] = DiagnosticValue.From(200L),
        },
        new HashSet<DiagnosticPermission> { DiagnosticPermission.OverrideInput },
        Stopwatch.GetTimestamp() + (10 * Stopwatch.Frequency));

    private sealed class ManualClock : TimeProvider
    {
        public long Timestamp { get; set; }

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Timestamp;
    }
}
