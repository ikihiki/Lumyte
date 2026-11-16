using BenchmarkDotNet.Attributes;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.UI.Automation;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Benchmark;

/// <summary>Bounded local diagnostics only; no transport, socket or screenshot simulation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Diagnostic")]
public class DiagnosticsBenchmarks
{
    private readonly Dictionary<string, DiagnosticValue> _arguments = [];
    private readonly HashSet<DiagnosticPermission> _permissions = [DiagnosticPermission.Observe];
    private UiAutomationDiagnosticsRuntime _runtime = null!;
    private DiagnosticOperationSet _operations = null!;
    private UiSurface _surface = null!;
    private Host _host = null!;
    private UiAutomationDiagnosticRegistration _registration = null!;
    private DiagnosticOperationContext _context = null!;

    /// <summary>Creates one owner-local runtime and an explicitly redacted subtree.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var redacted = new RedactedPanel { TestId = "private" };
        redacted.Children.Initialize(Button(testId: "secret"));
        _surface = new(Panel()[Button(testId: "public", width: 20, height: 10), redacted]);
        _surface.SetViewport(new(100, 100));
        _surface.UpdateLayout();
        _host = new(_surface);
        _runtime = new(TimeProvider.System, new() { MaximumTargets = 2, MaximumJobs = 2, MaximumRunningJobs = 1, MaximumArtifactBytes = 4096, MaximumStoredArtifactBytes = 4096 });
        _registration = _runtime.Register("benchmark", _surface, _host);
        _operations = new(new UiAutomationDiagnostics(_runtime));
        _context = new(Guid.NewGuid(), Guid.NewGuid(), new(1, 0), "benchmark", null, default);
    }

    /// <summary>Copies actual generated local operation metadata.</summary>
    /// <returns>The detached metadata.</returns>
    [Benchmark]
    public IReadOnlyList<OperationDescriptor> DiagnosticOperationSetCatalog() => _operations.Catalog;

    /// <summary>Executes the permission-checked targets operation and bounded JSON result.</summary>
    /// <returns>The actual generated dispatch result.</returns>
    [Benchmark]
    public DiagnosticOperationResult UiAutomationDiagnosticsTargets() => _operations.Invoke("targets", _arguments, _context, _permissions);

    /// <summary>Checks the real dispatch permission boundary without granting input rights.</summary>
    /// <returns>The forbidden generated operation result.</returns>
    public DiagnosticOperationResult DeniedControl() => _operations.Invoke("control-acquire", _arguments, _context, _permissions);

    /// <summary>Captures actual public redaction semantics separately from ordinary frames.</summary>
    /// <returns>The detached redacted semantic tree.</returns>
    [Benchmark]
    public UiAutomationSnapshot UiSurfaceRedactedSnapshot() => _surface.CaptureAutomationSnapshot();

    /// <summary>Replaces one registered target using public reservation/quiesce/commit.</summary>
    /// <returns>Full target exchange completion.</returns>
    [Benchmark]
    public async Task UiAutomationDiagnosticTargetExchangeAsync()
    {
        await using UiAutomationDiagnosticReplacement replacement = _runtime.ReserveReplacement("benchmark", _registration, _surface, _host);
        await replacement.QuiesceAsync(null, default);
        _registration = replacement.Commit();
    }

    /// <summary>Releases local registration before runtime and surface.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _registration.Dispose();
        _runtime.BeginStop();
        _runtime.Advance(new(2, 0));
        _runtime.Dispose();
        _surface.Dispose();
    }

    private sealed class RedactedPanel : Definitions.Panel
    {
        protected override UiElement CreateElement() => new RedactedElement(this);
    }

    private sealed class RedactedElement(RedactedPanel widget) : PanelElement(widget)
    {
        protected override UiAutomationExposure AutomationExposure => UiAutomationExposure.RedactedSubtree;
    }

    private sealed class Host(UiSurface surface) : IUiAutomationHost
    {
        public TimeProvider TimeProvider => TimeProvider.System;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action(surface));

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
        {
            surface.UpdateLayout();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Observation-only fixture.");
    }
}
