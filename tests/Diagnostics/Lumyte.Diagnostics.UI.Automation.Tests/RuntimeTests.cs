using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.Diagnostics.UI.Automation.Tests.Harness;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks owner-pumped admission, permissions, lease cleanup, replay protection and bounded retained artifacts.</summary>
public sealed class RuntimeTests
{
    /// <summary>Checks admission never executes input inside the operation, and forgotten sequences never execute again.</summary>
    [Fact]
    public void InputRunsOnlyAfterPumpAndSequenceSurvivesForget()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Guid job = Job(h.Start(Plan("click"), control));
        Assert.Equal(0, h.Clicks);
        Assert.Equal(job, Job(h.Start(Plan("click"), control)));
        Assert.Equal("action-id-conflict", h.Start(Plan("focus"), control).Code);
        Assert.Equal("sequence-gap", h.Start(Plan("click"), control, sequence: 3).Code);
        Assert.Equal("Succeeded", h.Complete(job, true)["state"].String);
        Assert.Equal(1, h.Clicks);
        Success(h.Call("action-forget", Args(("job-id", job.ToString("D")))));
        Assert.Equal("action-forgotten", h.Start(Plan("click"), control).Code);
        Assert.Equal("Succeeded", h.Complete(Job(h.Start(Plan("click"), control, sequence: 2)), true)["state"].String);
        Assert.Equal(2, h.Clicks);
    }

    /// <summary>Checks trusted owners and distinct Observe/OverrideInput operation descriptors prevent privilege confusion.</summary>
    [Fact]
    public void PermissionsOwnersAndKindsCannotBeSpoofed()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Dictionary<string, DiagnosticValue> acquire = Args(("target-id", h.Registration.TargetId.ToString("D")), ("duration-ms", 5000L));
        Assert.Equal("forbidden", h.Call("control-acquire", acquire, permission: DiagnosticPermission.Observe).Code);
        Assert.Equal("invalid-plan", h.Start(Plan("click")).Code);
        Assert.Equal("invalid-plan", h.Start(Plan("snapshot"), control).Code);
        Guid job = Job(h.Start(Plan("click"), control));
        Dictionary<string, DiagnosticValue> query = Args(("job-id", job.ToString("D")));
        Assert.Equal("not-found", h.Call("action-get", query, actor: "other").Code);
        Assert.Equal("not-found", h.Call("action-get", query, session: Guid.NewGuid()).Code);
        Assert.Equal("not-found", h.Call("observe-get", query).Code);
        Assert.Equal("forbidden", h.Call("action-get", query, permission: DiagnosticPermission.Observe).Code);
        Assert.Equal("forbidden", h.Call("artifact-read", Args(("job-id", job.ToString("D")), ("artifact-id", Guid.NewGuid().ToString("D")), ("offset", 0L), ("count", 1L)), permission: DiagnosticPermission.OverrideInput).Code);
    }

    /// <summary>Checks release revisions, cancellation completion and reacquisition exclude unfinished host cleanup.</summary>
    [Fact]
    public void ReleaseWaitsForJobCleanupAndChecksRevision()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Guid job = Job(h.Start(Plan("click", "missing"), control));
        h.Step();
        Dictionary<string, DiagnosticValue> args = Args(("control-id", control.ToString("D")));
        long revision = Success(h.Call("control-get", args))["revision"].Int64;
        Assert.Equal("revision-required", h.Call("control-release", args).Code);
        Assert.Equal("conflict", h.Call("control-release", args, revision: revision + 1).Status);
        Assert.Equal("Releasing", Success(h.Call("control-release", args, revision: revision))["state"].String);
        Assert.Equal("busy", h.Call("control-acquire", Args(("target-id", h.Registration.TargetId.ToString("D")), ("duration-ms", 5000L)), actor: "other").Code);
        Assert.Equal("Canceled", h.Complete(job, true)["state"].String);
        Assert.NotEqual(control, h.Acquire("other"));
        Assert.Equal(0, h.Clicks);
    }

    /// <summary>Checks control expiration prevents input and is independent of diagnostic frame timestamps.</summary>
    [Fact]
    public void ExpirationUsesMonotonicClockAndDoesNotResubmit()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Guid job = Job(h.Start(Plan("click", "missing"), control, timeout: 10000));
        h.Step(TimeSpan.FromSeconds(6));
        Assert.Equal("Canceled", h.Complete(job, true)["state"].String);
        Assert.Equal("lease-gone", h.Start(Plan("click"), control, sequence: 2).Code);
        Assert.Equal(0, h.Clicks);
    }

    /// <summary>Checks RPC cancellation is not retained as the accepted job's lifetime token.</summary>
    [Fact]
    public void AcceptedJobOutlivesItsRpcCancellation()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        using var rpc = new CancellationTokenSource();
        DiagnosticOperationContext context = new(Guid.NewGuid(), h.Session, new(0, 0), "operator", null, rpc.Token);
        Dictionary<string, DiagnosticValue> args = Args(("target-id", h.Registration.TargetId.ToString("D")), ("control-id", control.ToString("D")), ("sequence", 1L), ("plan-json", UiRemoteSchema.SerializePlan(Plan("click"))), ("job-timeout-ms", 1000L));
        Guid job = Job(h.Operations.Invoke("action-start", args, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.OverrideInput }));
        rpc.Cancel();
        Assert.Equal("Succeeded", h.Complete(job, true)["state"].String);
        Assert.Equal(1, h.Clicks);
    }

    /// <summary>Checks session closure bypasses a full ordinary job budget and unregister waits for cleanup.</summary>
    [Fact]
    public void SessionClosureAndUnregistrationDoNotLoseCancellation()
    {
        using var h = new Harness(new() { MaximumJobs = 1, MaximumRunningJobs = 1 });
        Guid control = h.Acquire();
        Guid job = Job(h.Start(Plan("click", "missing"), control));
        Assert.Equal("busy", h.Start(Plan("count")).Code);
        h.Runtime.NotifySessionClosed(h.Session);
        h.Registration.Dispose();
        Assert.False(h.Registration.IsReleased);
        Assert.Equal("Canceled", h.Complete(job, true)["state"].String);
        Assert.True(h.Registration.IsReleased);
        Assert.Equal("not-found", h.Start(Plan("count")).Code);
    }

    /// <summary>Checks snapshot encoding contains only detached wire data and immutable byte chunks.</summary>
    [Fact]
    public void SnapshotArtifactsUseDecimalIdsAndBoundedByteOffsets()
    {
        using var h = new Harness();
        Guid job = Job(h.Start(Plan("snapshot")));
        IReadOnlyDictionary<string, DiagnosticValue> result = h.Complete(job, false);
        var artifact = Guid.Parse(result["result-artifact-id"].String!);
        Dictionary<string, DiagnosticValue> args = Args(("job-id", job.ToString("D")), ("artifact-id", artifact.ToString("D")), ("offset", 0L), ("count", 65536L));
        IReadOnlyDictionary<string, DiagnosticValue> chunk = Success(h.Call("artifact-read", args));
        byte[] bytes = Convert.FromBase64String(chunk["base64"].String!);
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        Assert.Equal(h.Surface.CaptureAutomationSnapshot().SurfaceId, snapshot.SurfaceId);
        Assert.True(ulong.TryParse(snapshot.Sequence, out _));
        Assert.All(snapshot.Nodes, node => Assert.True(ulong.TryParse(node.Id, out _)));
        Assert.Equal(chunk["base64"], Success(h.Call("artifact-read", args))["base64"]);
        args["offset"] = DiagnosticValue.From(bytes.LongLength);
        Assert.Equal(0, Success(h.Call("artifact-read", args))["bytes"].Int64);
        args["offset"] = DiagnosticValue.From(long.MaxValue);
        Assert.Equal("invalid-range", h.Call("artifact-read", args).Code);
        args["offset"] = DiagnosticValue.From(0L);
        Assert.Equal("not-found", h.Call("artifact-read", args, actor: "other").Code);
    }

    /// <summary>Checks screenshot capability survives the job wrapper and copied artifact bytes cannot be changed by the host.</summary>
    [Fact]
    public void ScreenshotsAreOptInAndStoredAsFixedBytes()
    {
        using var h = new Harness(screenshots: true);
        Guid job = Job(h.Start(Plan("screenshot")));
        IReadOnlyDictionary<string, DiagnosticValue> done = h.Complete(job, false);
        string artifact = done["result-artifact-id"].String!;
        byte original = h.Image[0];
        h.Image[0] = 42;
        IReadOnlyDictionary<string, DiagnosticValue> chunk = Success(h.Call("artifact-read", Args(("job-id", job.ToString("D")), ("artifact-id", artifact), ("offset", 0L), ("count", 1L))));
        Assert.Equal(original, Convert.FromBase64String(chunk["base64"].String!)[0]);
        using var denied = new Harness();
        Assert.Equal("screenshot-not-allowed", denied.Start(Plan("screenshot")).Code);
    }

    /// <summary>Checks result overflow is rejected without truncation and capacity is returned only after terminal Forget.</summary>
    [Fact]
    public void ResultCapacityAndRetentionAreBounded()
    {
        using var h = new Harness(new() { MaximumJobs = 1, MaximumRunningJobs = 1, MaximumArtifactBytes = 2, MaximumStoredArtifactBytes = 2, Retention = TimeSpan.FromSeconds(1) }, screenshots: true);
        Guid job = Job(h.Start(Plan("screenshot")));
        Assert.Equal("result-too-large", h.Complete(job, false)["code"].String);
        Assert.Equal("busy", h.Start(Plan("count")).Code);
        h.Step(TimeSpan.FromSeconds(2));
        Assert.Equal("not-found", h.Call("observe-get", Args(("job-id", job.ToString("D")))).Code);
        Assert.Equal("Succeeded", h.Complete(Job(h.Start(Plan("count"))), false)["state"].String);
    }

    /// <summary>Checks disabled DI registration publishes no targets and starts no UI or jobs.</summary>
    [Fact]
    public void DisabledSubsystemHasNoTargetOrBackgroundWork()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = false);
        services.AddDiagnosticExecutionPoint<Point>("ui.before-automation");
        services.AddUiAutomationDiagnostics<Point>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        UiAutomationDiagnosticsRuntime runtime = scope.ServiceProvider.GetRequiredService<UiAutomationDiagnosticsRuntime>();
        using var h = new Harness();
        using UiAutomationDiagnosticRegistration registration = runtime.Register("disabled", h.Surface, new ThrowHost());
        Assert.True(registration.IsReleased);
        Assert.Equal("[]", runtime.Targets().Value.Json);
        runtime.Advance(new(1, 0));
        runtime.BeginStop();
        Assert.True(runtime.IsStopped);
    }

    /// <summary>Checks queued and running deadlines complete cancellation before terminal status.</summary>
    /// <param name="running">Whether the operation has begun waiting for UI state.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JobDeadlineIsIndependentOfRpcAndControl(bool running)
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Guid job = Job(h.Start(Plan("focus", "missing"), control));
        if (running)
        {
            h.Step();
            Assert.True(Success(h.Call("action-get", Args(("job-id", job.ToString("D")))))["input-may-have-been-sent"].Boolean);
        }

        h.Step(TimeSpan.FromSeconds(2));
        IReadOnlyDictionary<string, DiagnosticValue> result = h.Complete(job, true);
        Assert.Equal("TimedOut", result["state"].String);
        Assert.Equal("job-timeout", result["code"].String);
        Assert.Equal(0, h.Clicks);
    }

    /// <summary>Checks cancellation of a pending screenshot does not release the registration before capture returns.</summary>
    [Fact]
    public void ScreenshotCancellationKeepsHostOwnershipUntilActualCompletion()
    {
        using var h = new Harness(screenshots: true);
        h.Capture = new();
        Guid job = Job(h.Start(Plan("screenshot")));
        h.Step();
        h.Step(TimeSpan.FromSeconds(2));
        h.Registration.Dispose();
        Assert.Equal("CancelRequested", Success(h.Call("observe-get", Args(("job-id", job.ToString("D")))))["state"].String);
        Assert.False(h.Registration.IsReleased);
        h.Capture.SetResult(h.Image);
        Assert.Equal("TimedOut", h.Complete(job, false)["state"].String);
        Assert.True(h.Registration.IsReleased);
    }

    /// <summary>Checks absolute retention expiry applies to reads before the next Advance.</summary>
    [Fact]
    public void ReadsNeverExtendOrBypassAbsoluteRetention()
    {
        using var h = new Harness(new() { Retention = TimeSpan.FromSeconds(1) });
        Guid job = Job(h.Start(Plan("snapshot")));
        IReadOnlyDictionary<string, DiagnosticValue> result = h.Complete(job, false);
        string artifact = result["result-artifact-id"].String!;
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("not-found", h.Call("artifact-read", Args(("job-id", job.ToString("D")), ("artifact-id", artifact), ("offset", 0L), ("count", 1L))).Code);
        Assert.Equal("not-found", h.Call("observe-get", Args(("job-id", job.ToString("D")))).Code);
    }

    /// <summary>Checks acquisition by the current owner does not renew its lifetime and revision-protected renewal does.</summary>
    [Fact]
    public void RenewRequiresFreshRevisionAndAcquireDoesNotExtendLifetime()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        h.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(control, h.Acquire());
        Dictionary<string, DiagnosticValue> args = Args(("control-id", control.ToString("D")));
        IReadOnlyDictionary<string, DiagnosticValue> current = Success(h.Call("control-get", args));
        Assert.InRange(current["remaining-milliseconds"].Int64, 999, 1000);
        args["duration-ms"] = DiagnosticValue.From(5000L);
        Success(h.Call("control-renew", args, revision: current["revision"].Int64));
        Assert.Equal("conflict", h.Call("control-renew", args, revision: current["revision"].Int64).Status);
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Succeeded", h.Complete(Job(h.Start(Plan("click"), control)), true)["state"].String);
    }

    /// <summary>Checks only the configured number of jobs begin in one Advance.</summary>
    [Fact]
    public void RunningCapacityDoesNotStartEveryQueuedJob()
    {
        using var h = new Harness(new() { MaximumRunningJobs = 1 });
        Guid first = Job(h.Start(Plan("count")));
        Guid second = Job(h.Start(Plan("count")));
        h.Step();
        Assert.Equal("Running", Success(h.Call("observe-get", Args(("job-id", first.ToString("D")))))["state"].String);
        Assert.Equal("Queued", Success(h.Call("observe-get", Args(("job-id", second.ToString("D")))))["state"].String);
        Assert.Equal("Succeeded", h.Complete(second, false)["state"].String);
    }

    /// <summary>Checks screenshot serialization does not overtake an unfinished capture on the same target.</summary>
    [Fact]
    public void ConcurrentScreenshotAdmissionIsBoundedPerTarget()
    {
        using var h = new Harness(screenshots: true);
        h.Capture = new();
        Guid first = Job(h.Start(Plan("screenshot")));
        h.Step();
        Assert.Equal("busy", h.Start(Plan("screenshot")).Code);
        h.Capture.SetResult(h.Image);
        Assert.Equal("Succeeded", h.Complete(first, false)["state"].String);
    }

    /// <summary>Checks enabled DI uses the same configured DiagnosticOptions instance as the existing service.</summary>
    [Fact]
    public void EnabledDiPublishesExplicitlyRegisteredTargets()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = true);
        services.AddDiagnosticExecutionPoint<Point>("ui.before-automation");
        services.AddUiAutomationDiagnostics<Point>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        UiAutomationDiagnosticsRuntime runtime = scope.ServiceProvider.GetRequiredService<UiAutomationDiagnosticsRuntime>();
        using var h = new Harness();
        var host = new Lumyte.UI.SampleSupport.UiSampleAutomationHost(h.Surface, static _ => ValueTask.CompletedTask);
        using UiAutomationDiagnosticRegistration registration = runtime.Register("enabled", h.Surface, host);
        Assert.False(registration.IsReleased);
        Assert.Contains("enabled", runtime.Targets().Value.Json, StringComparison.Ordinal);
        runtime.BeginStop();
        Assert.True(runtime.IsStopped);
        Assert.True(host.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>Checks JSON encoding stops at the configured result budget rather than publishing a truncated snapshot.</summary>
    [Fact]
    public void LargeSnapshotsAreRejectedDuringEncoding()
    {
        using var h = new Harness(new() { MaximumArtifactBytes = 256, MaximumStoredArtifactBytes = 512 });
        Guid job = Job(h.Start(Plan("snapshot")));
        IReadOnlyDictionary<string, DiagnosticValue> status = h.Complete(job, false);
        Assert.Equal("Failed", status["state"].String);
        Assert.Equal("result-too-large", status["code"].String);
        Assert.Empty(status["result-artifact-id"].String!);
    }

    private sealed class Point;

    private sealed class ThrowHost : Lumyte.UI.Automation.IUiAutomationHost
    {
        public TimeProvider TimeProvider => throw new InvalidOperationException();

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<Lumyte.UI.Automation.IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
