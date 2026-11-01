using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Lumyte.UI.Automation;
using Lumyte.UI.Automation.Tests;
using Lumyte.UI.SampleSupport;
using Xunit;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

internal sealed class Harness : IDisposable
{
    private readonly List<TaskCompletionSource> _frames = [];
    private readonly UiSampleAutomationHost _host;
    private long _frame;

    internal Harness(UiAutomationDiagnosticsOptions? options = null, bool screenshots = false)
    {
        Runtime = new(Clock, options ?? new());
        Surface = UiAutomationExercise.CreateSurface(() => Clicks++);
        Surface.SetViewport(new(240, 160));
        _host = new(Surface, WaitFrameAsync, Clock);
        IUiAutomationHost host = screenshots ? new UiSampleScreenshotHost(_host, CaptureAsync) : _host;
        Registration = Runtime.Register("settings", Surface, host, new() { AllowInput = true, AllowScreenshots = screenshots });
        Operations = new(new UiAutomationDiagnostics(Runtime));
    }

    internal ManualClock Clock { get; } = new();

    internal UiAutomationDiagnosticsRuntime Runtime { get; }

    internal UiSurface Surface { get; }

    internal UiAutomationDiagnosticRegistration Registration { get; }

    internal DiagnosticOperationSet Operations { get; }

    internal Guid Session { get; } = Guid.NewGuid();

    internal int Clicks { get; private set; }

    internal byte[] Image { get; set; } = [137, 80, 78, 71, 13, 10, 26, 10];

    internal TaskCompletionSource<byte[]>? Capture { get; set; }

    public void Dispose()
    {
        Capture?.TrySetResult(Image);
        Runtime.BeginStop();
        for (int i = 0; !Runtime.IsStopped && i < 1000; i++)
        {
            Step(TimeSpan.Zero);
            Thread.Yield();
        }

        Assert.True(Runtime.IsStopped);
        Runtime.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Surface.Dispose();
    }

    internal static UiRemotePlan Plan(string kind, string id = "save") => new(1, kind, kind is "snapshot" or "screenshot" ? [] : [new("test-id", Value: id)]);

    internal static Guid Job(DiagnosticOperationResult result) => Guid.Parse(Success(result)["job-id"].String!);

    internal static IReadOnlyDictionary<string, DiagnosticValue> Success(DiagnosticOperationResult result)
    {
        Assert.True(result.Status == "success", result.Code + ": " + result.Message);
        return result.Values!;
    }

    internal static Dictionary<string, DiagnosticValue> Args(params (string Key, object Value)[] values) => values.ToDictionary(pair => pair.Key, pair => pair.Value is string text ? DiagnosticValue.From(text) : DiagnosticValue.From(Convert.ToInt64(pair.Value, System.Globalization.CultureInfo.InvariantCulture)));

    internal Guid Acquire(string actor = "operator") => Guid.Parse(Success(Call("control-acquire", Args(("target-id", Registration.TargetId.ToString("D")), ("duration-ms", 5000L)), actor: actor))["control-id"].String!);

    internal DiagnosticOperationResult Start(UiRemotePlan plan, Guid? control = null, long sequence = 1, string actor = "operator", long timeout = 1000)
    {
        Dictionary<string, DiagnosticValue> args = Args(("target-id", Registration.TargetId.ToString("D")), ("plan-json", UiRemoteSchema.SerializePlan(plan)), ("job-timeout-ms", timeout));
        if (control is { } id)
        {
            args.Add("control-id", DiagnosticValue.From(id.ToString("D")));
            args.Add("sequence", DiagnosticValue.From(sequence));
        }

        return Call(control is null ? "observe-start" : "action-start", args, actor: actor);
    }

    internal DiagnosticOperationResult Call(string operation, Dictionary<string, DiagnosticValue>? args = null, string actor = "operator", Guid? session = null, DiagnosticPermission? permission = null, long? revision = null, CancellationToken cancellationToken = default)
    {
        var context = new DiagnosticOperationContext(Guid.NewGuid(), session ?? Session, new(_frame, 0), actor, revision, cancellationToken);
        HashSet<DiagnosticPermission> permissions = permission is { } grant ? [grant] : [DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput];
        return Operations.Invoke(operation, args ?? [], context, permissions);
    }

    internal void Step(TimeSpan? elapsed = null)
    {
        Clock.Advance(elapsed ?? TimeSpan.FromMilliseconds(10));
        Runtime.Advance(new(_frame++, long.MaxValue));
        TaskCompletionSource[] previous = _frames.ToArray();
        _frames.Clear();
        Surface.UpdateLayout();
        foreach (TaskCompletionSource frame in previous)
        {
            frame.TrySetResult();
        }
    }

    internal IReadOnlyDictionary<string, DiagnosticValue> Complete(Guid job, bool action)
    {
        for (int i = 0; i < 1000; i++)
        {
            Step();
            IReadOnlyDictionary<string, DiagnosticValue> result = Success(Call(action ? "action-get" : "observe-get", Args(("job-id", job.ToString("D")))));
            if (result["state"].String is "Succeeded" or "Failed" or "Canceled" or "TimedOut")
            {
                return result;
            }

            Thread.Yield();
        }

        throw new InvalidOperationException("The deterministic job failed to finish.");
    }

    private ValueTask<byte[]> CaptureAsync(CancellationToken token) => Capture is null ? ValueTask.FromResult(Image) : new(Capture.Task);

    private ValueTask WaitFrameAsync(CancellationToken token)
    {
        var frame = new TaskCompletionSource();
        _frames.Add(frame);
        return new(frame.Task.WaitAsync(token));
    }
}
