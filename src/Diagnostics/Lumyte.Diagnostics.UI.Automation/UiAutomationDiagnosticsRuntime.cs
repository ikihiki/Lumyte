using System.Collections.Concurrent;
using System.Diagnostics;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Lumyte.UI.Automation;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns bounded UI jobs and detached results on one diagnostic execution owner.</summary>
public sealed partial class UiAutomationDiagnosticsRuntime : IDisposable
{
    private static readonly ActivitySource _activities = new("Lumyte.Diagnostics.UI.Automation");
    private readonly TimeProvider _clock;
    private readonly UiAutomationDiagnosticsOptions _options;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid, Target> _targets = [];
    private readonly Dictionary<Guid, Control> _controls = [];
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly Dictionary<Guid, Artifact> _artifacts = [];
    private readonly ConcurrentDictionary<Guid, long> _closedSessions = [];
    private long _artifactBytes;
    private int _closedCount;
    private int _closeOverflow;
    private bool _stopping;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UiAutomationDiagnosticsRuntime"/> class.</summary>
    /// <param name="clock">The monotonic acceptance and retention clock.</param>
    /// <param name="options">Positive bounded capacities and retention.</param>
    public UiAutomationDiagnosticsRuntime(TimeProvider clock, UiAutomationDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumTargets <= 0 || options.MaximumJobs <= 0 || options.MaximumRunningJobs <= 0 || options.MaximumRunningJobs > options.MaximumJobs
            || options.MaximumArtifactBytes <= 0 || options.MaximumArtifactBytes > int.MaxValue || options.MaximumStoredArtifactBytes < options.MaximumArtifactBytes || options.Retention <= TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid automation capacities.", nameof(options));
        }

        _clock = clock;
        _options = options with { };
    }

    /// <summary>Gets a value indicating whether stop admission, jobs and registration cleanup have completed.</summary>
    public bool IsStopped => _stopping && _targets.Count == 0 && _jobs.Values.All(job => job.Terminal);

    internal bool Enabled { get; set; } = true;

    internal ILogger? Logger { get; set; }

    internal Guid InstanceId { get; set; }

    /// <summary>Registers one explicitly published surface on the owner; captures identity once when enabled.</summary>
    /// <param name="name">A unique lowercase ASCII identifier of 1 to 128 characters.</param>
    /// <param name="surface">The borrowed surface.</param>
    /// <param name="host">The borrowed host for exactly this surface.</param>
    /// <param name="options">Input and screenshot opt-ins, both false by default.</param>
    /// <returns>A registration released only after its jobs finish cleanup.</returns>
    public UiAutomationDiagnosticRegistration Register(string name, UiSurface surface, IUiAutomationHost host, UiAutomationTargetOptions? options = null)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Invalid target name.", nameof(name));
        }

        if (_stopping)
        {
            throw new InvalidOperationException("The runtime is stopping.");
        }

        var id = Guid.NewGuid();
        if (!Enabled)
        {
            return new(id, static () =>
            {
            })
            { IsReleased = true };
        }

        if (_targets.Count >= _options.MaximumTargets || _targets.Values.Any(target => target.Name == name || ReferenceEquals(target.Surface, surface)))
        {
            throw new InvalidOperationException("Duplicate target or capacity exceeded.");
        }

        Guid surfaceId = surface.CaptureAutomationSnapshot().SurfaceId;
        ValueTask<Guid> check = host.RunOnUiThreadAsync(candidate => ReferenceEquals(candidate, surface) ? candidate.CaptureAutomationSnapshot().SurfaceId : Guid.Empty);
        if (!check.IsCompletedSuccessfully || check.Result != surfaceId)
        {
            throw new ArgumentException("Registration must run synchronously on the host UI owner for the same surface.", nameof(host));
        }

        var registration = new UiAutomationDiagnosticRegistration(id, () => Unregister(id));
        _targets.Add(id, new(id, name, surface, host, options ?? new(), registration));
        return registration;
    }

    /// <summary>Registers an explicit popup target in the same input control group as its parent.</summary>
    /// <param name="parent">A live registration from this runtime.</param>
    /// <param name="popupId">The nonreused popup identity.</param>
    /// <param name="name">The unique target name.</param>
    /// <param name="surface">The borrowed popup surface.</param>
    /// <param name="host">The same routing host used for physical input and cleanup.</param>
    /// <param name="options">Explicit input and screenshot opt-ins.</param>
    /// <returns>A registration which must drain before popup drawing teardown.</returns>
    public UiAutomationDiagnosticRegistration RegisterPopup(UiAutomationDiagnosticRegistration parent, Guid popupId, string name, UiSurface surface, IUiAutomationHost host, UiAutomationTargetOptions? options = null)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(parent);
        if (popupId == Guid.Empty)
        {
            throw new ArgumentException("A popup identity is required.", nameof(popupId));
        }

        if (!Enabled)
        {
            return Register(name, surface, host, options);
        }

        if (!_targets.TryGetValue(parent.TargetId, out Target? target) || !ReferenceEquals(target.Registration, parent) || !target.Accepting || _targets.Values.Any(t => t.PopupId == popupId))
        {
            throw new ArgumentException("Unknown parent or duplicate popup identity.");
        }

        if (host is not IUiAutomationRoutingHost || _targets[target.ControlGroupId].Host is not IUiAutomationRoutingHost)
        {
            throw new ArgumentException("Popup and root registrations must use the shared window routing host.", nameof(host));
        }

        if (_targets.Count >= _options.MaximumTargets)
        {
            _targets[target.ControlGroupId].AutomationCode = "automation-unavailable";
            throw new InvalidOperationException("Diagnostic target capacity exceeded; popup display may continue without registration.");
        }

        var effective = new UiAutomationTargetOptions
        {
            AllowInput = target.Options.AllowInput && (options?.AllowInput ?? false),
            AllowTextInput = target.Options.AllowTextInput && (options?.AllowTextInput ?? false),
            AllowTextInspection = target.Options.AllowTextInspection && (options?.AllowTextInspection ?? false),
            AllowScreenshots = target.Options.AllowScreenshots && (options?.AllowScreenshots ?? false),
        };
        UiAutomationDiagnosticRegistration registration = Register(name, surface, host, effective);
        if (_targets.TryGetValue(registration.TargetId, out Target? added))
        {
            added.ParentTargetId = target.Id;
            added.PopupId = popupId;
            added.ControlGroupId = target.ControlGroupId;
        }

        return registration;
    }

    /// <summary>Processes expiry/cancellation, completed results and new jobs after Diagnostics.Pump, never waiting for a Task.</summary>
    /// <param name="frame">The engine frame, not the deadline clock.</param>
    public void Advance(DiagnosticFrame frame)
    {
        Verify();
        if (!Enabled)
        {
            return;
        }

        if (Volatile.Read(ref _closeOverflow) != 0 && !_stopping)
        {
            BeginStop();
        }

        foreach (Control control in _controls.Values)
        {
            if (control.State == "Active" && (_closedSessions.ContainsKey(control.Session) || Remaining(control.Started, control.Duration) <= TimeSpan.Zero))
            {
                ReleaseControl(control, _closedSessions.ContainsKey(control.Session) ? "session-closed" : "lease-expired");
            }
        }

        foreach (Job job in _jobs.Values)
        {
            if (!job.Terminal && _closedSessions.ContainsKey(job.Session))
            {
                Cancel(job, "session-closed");
            }

            if (!job.Terminal && Remaining(job.Started, job.Timeout) <= TimeSpan.Zero)
            {
                Cancel(job, "job-timeout");
            }

            if (job.State == "CancelRequested" && !job.Cancellation.IsCancellationRequested)
            {
                try
                {
                    job.Cancellation.Cancel();
                }
                catch (Exception)
                {
                    Audit("cancel-callback-failed", job);
                }
            }

            if (job.Task is { IsCompleted: true } task)
            {
                Outcome outcome;
                try
                {
                    outcome = task.GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    outcome = new(null, string.Empty, [], "action-failed");
                }

                Finish(job, outcome);
            }
            else if (job.State == "CancelRequested" && job.Task is null)
            {
                Finish(job, new(null, string.Empty, [], "canceled"));
            }
        }

        Settle();
        foreach (Job job in _jobs.Values.Where(job => job.Terminal && _clock.GetElapsedTime(job.Completed) >= _options.Retention).ToArray())
        {
            Forget(job);
        }

        foreach ((Guid session, long closed) in _closedSessions)
        {
            if (_clock.GetElapsedTime(closed) >= _options.Retention && !_jobs.Values.Any(job => job.Session == session && !job.Terminal)
                && !_controls.Values.Any(control => control.Session == session && control.State != "Released") && _closedSessions.TryRemove(session, out _))
            {
                Interlocked.Decrement(ref _closedCount);
            }
        }

        int running = _jobs.Values.Count(job => job.Task is not null);
        foreach (Job job in _jobs.Values.Where(job => job.State == "Queued").ToArray())
        {
            if (job.State != "Queued" || _stopping)
            {
                continue;
            }

            if (running >= _options.MaximumRunningJobs)
            {
                break;
            }

            job.State = "Running";
            job.Revision++;
            Audit("start", job);
            job.Task = ExecuteAsync(job);
            running++;
        }
    }

    /// <summary>Records a bounded coalesced session-close notification without touching UI; excess notifications fail closed.</summary>
    /// <param name="sessionId">The trusted Agent session that ended.</param>
    public void NotifySessionClosed(Guid sessionId)
    {
        if (Volatile.Read(ref _disposed) || _closedSessions.ContainsKey(sessionId))
        {
            return;
        }

        if (Interlocked.Increment(ref _closedCount) > (long)_options.MaximumJobs + _options.MaximumTargets)
        {
            Interlocked.Decrement(ref _closedCount);
            Interlocked.Exchange(ref _closeOverflow, 1);
            return;
        }

        if (!_closedSessions.TryAdd(sessionId, _clock.GetTimestamp()))
        {
            Interlocked.Decrement(ref _closedCount);
        }
    }

    /// <summary>Stops admission and marks all jobs for owner-driven cancellation; keep advancing until IsStopped.</summary>
    public void BeginStop()
    {
        Verify();
        _stopping = true;
        foreach (Target target in _targets.Values)
        {
            target.Accepting = false;
        }

        foreach (Control control in _controls.Values)
        {
            ReleaseControl(control, "stopping");
        }

        foreach (Job job in _jobs.Values)
        {
            Cancel(job, "stopping");
        }

        Settle();
    }

    /// <summary>Releases only detached diagnostic state after job cleanup; never disposes a host, surface or Window.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BeginStop();
        if (!IsStopped)
        {
            throw new InvalidOperationException("Continue Advance until all host cleanup completes before disposal.");
        }

        foreach (Job job in _jobs.Values.ToArray())
        {
            Forget(job);
        }

        _controls.Clear();
        _closedSessions.Clear();
        _disposed = true;
    }

    private TimeSpan Remaining(long started, TimeSpan duration) => duration - _clock.GetElapsedTime(started);

    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Run diagnostic operations and Advance on their execution owner.");
        }
    }

    private void Unregister(Guid id)
    {
        if (_disposed)
        {
            return;
        }

        Verify();
        if (_targets.TryGetValue(id, out Target? target))
        {
            target.Accepting = false;
            foreach (Target child in _targets.Values.Where(t => t.ParentTargetId == id).ToArray())
            {
                Unregister(child.Id);
            }

            foreach (Job job in _jobs.Values.Where(job => job.TargetId == id))
            {
                Cancel(job, "target-released");
            }

            if (target.Control is not null)
            {
                ReleaseControl(target.Control, "target-released");
            }

            Settle();
        }
    }

    private void Cancel(Job job, string code)
    {
        if (job.Terminal || job.State == "CancelRequested")
        {
            return;
        }

        job.State = "CancelRequested";
        job.Code = code;
        job.Revision++;
        Audit("cancel", job);
    }

    private void ReleaseControl(Control control, string code)
    {
        if (control.State != "Active")
        {
            return;
        }

        AuditControl("release", control);
        control.State = "Releasing";
        control.Revision++;
        foreach (Job job in _jobs.Values.Where(job => job.ControlId == control.Id || (job.Session == control.Session && job.Actor == control.Actor && _targets.TryGetValue(job.TargetId, out Target? target) && target.ControlGroupId == control.TargetId)))
        {
            Cancel(job, code);
        }
    }

    private void Settle()
    {
        foreach (Control control in _controls.Values)
        {
            if (control.State == "Releasing" && !_jobs.Values.Any(job => job.ControlId == control.Id && !job.Terminal))
            {
                AuditControl("released", control);
                control.State = "Released";
                control.Revision++;
            }
        }

        foreach (Target target in _targets.Values.Where(target => !target.Accepting && !_targets.Values.Any(child => child.ParentTargetId == target.Id) && !_jobs.Values.Any(job => job.TargetId == target.Id && !job.Terminal)).ToArray())
        {
            target.Registration.IsReleased = true;
            _targets.Remove(target.Id);
            if (target.Control is not null)
            {
                _controls.Remove(target.Control.Id);
            }
        }
    }

    private void AuditControl(string name, Control control)
    {
        using IDisposable? scope = Logger?.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = InstanceId.ToString("D") });
        using Activity? activity = _activities.StartActivity("ui.automation.control." + name);
        activity?.SetTag("lumyte.instance.id", InstanceId.ToString("D"));
        activity?.SetTag("control", control.Id.ToString("D"));
        activity?.SetTag("target", control.TargetId.ToString("D"));
        activity?.SetTag("actor", control.Actor);
        activity?.SetTag("session", control.Session.ToString("D"));
        Logger?.LogInformation("UI control {Event}: control={Control} target={Target} session={Session} actor={Actor}", name, control.Id, control.TargetId, control.Session, control.Actor);
    }

    private void Audit(string name, Job job)
    {
        using IDisposable? scope = Logger?.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = InstanceId.ToString("D") });
        using Activity? activity = _activities.StartActivity("ui.automation." + name);
        activity?.SetTag("lumyte.instance.id", InstanceId.ToString("D"));
        activity?.SetTag("target", job.TargetId.ToString("D"));
        activity?.SetTag("actor", job.Actor);
        activity?.SetTag("session", job.Session.ToString("D"));
        activity?.SetTag("sequence", job.Sequence);
        activity?.SetTag("input-may-have-been-sent", job.InputMayHaveBeenSent);
        Logger?.LogInformation("UI automation {Event}: job={Job} target={Target} session={Session} actor={Actor} sequence={Sequence} input={Input}", name, job.Id, job.TargetId, job.Session, job.Actor, job.Sequence, job.InputMayHaveBeenSent);
    }

    private sealed class Target(Guid id, string name, UiSurface surface, IUiAutomationHost host, UiAutomationTargetOptions options, UiAutomationDiagnosticRegistration registration)
    {
        internal Guid Id { get; } = id;

        internal string Name { get; } = name;

        internal UiSurface Surface { get; } = surface;

        internal IUiAutomationHost Host { get; } = host;

        internal UiAutomationTargetOptions Options { get; } = options with { };

        internal UiAutomationDiagnosticRegistration Registration { get; } = registration;

        internal bool Accepting { get; set; } = true;

        internal string AutomationCode { get; set; } = string.Empty;

        internal Guid? ParentTargetId { get; set; }

        internal Guid? PopupId { get; set; }

        internal Guid ControlGroupId { get; set; } = id;

        internal Control? Control { get; set; }
    }

    private sealed class Control(Guid target, Guid session, string actor, long started, TimeSpan duration)
    {
        internal Guid Id { get; } = Guid.NewGuid();

        internal Guid TargetId { get; } = target;

        internal Guid Session { get; } = session;

        internal string Actor { get; } = actor;

        internal long Started { get; set; } = started;

        internal TimeSpan Duration { get; set; } = duration;

        internal string State { get; set; } = "Active";

        internal long Revision { get; set; } = 1;

        internal long NextSequence { get; set; } = 1;
    }

    private sealed class Job(Guid target, Guid session, string actor, Guid? control, long sequence, string fingerprint, UiRemotePlan plan, long started, TimeSpan timeout, IUiAutomationHost host)
    {
        private int _inputSent;

        internal Guid Id { get; } = Guid.NewGuid();

        internal Guid TargetId { get; } = target;

        internal Guid Session { get; } = session;

        internal string Actor { get; } = actor;

        internal Guid? ControlId { get; } = control;

        internal long Sequence { get; } = sequence;

        internal string Fingerprint { get; } = fingerprint;

        internal UiRemotePlan? Plan { get; set; } = plan;

        internal long Started { get; } = started;

        internal TimeSpan Timeout { get; } = timeout;

        internal IUiAutomationHost? Host { get; set; } = host;

        internal UiSurface? RegisteredSurface { get; set; }

        internal CancellationTokenSource Cancellation { get; } = new();

        internal string State { get; set; } = "Queued";

        internal string Code { get; set; } = string.Empty;

        internal long Revision { get; set; } = 1;

        internal Task<Outcome>? Task { get; set; }

        internal long Completed { get; set; }

        internal Guid? ResultId { get; set; }

        internal Guid? TraceId { get; set; }

        internal bool InputMayHaveBeenSent => Volatile.Read(ref _inputSent) != 0;

        internal bool Terminal => State is "Succeeded" or "Failed" or "Canceled" or "TimedOut";

        internal void MarkInputSent() => Interlocked.Exchange(ref _inputSent, 1);
    }

    private sealed record Artifact(Guid Id, Guid JobId, byte[] Bytes, string ContentType, string Hash);

    private sealed record Outcome(byte[]? Result, string ContentType, string[] Trace, string Code);
}
