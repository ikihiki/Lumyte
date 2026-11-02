using Lumyte.UI;
using Lumyte.UI.Automation;

namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Coordinates staged publication and retained cleanup on the UI owner.</summary>
public sealed partial class UiAutomationDiagnosticsRuntime
{
    private static readonly AsyncLocal<Job?> _currentOperation = new();
    private readonly Dictionary<Guid, Replacement> _replacements = [];

    /// <summary>Reserves a hidden candidate, reusing the current registration's target budget.</summary>
    /// <param name="name">The current name, or a new unique name for initial publication.</param>
    /// <param name="current">The current live registration, or null initially.</param>
    /// <param name="candidate">The private candidate surface.</param>
    /// <param name="candidateHost">Its borrowed routing host.</param>
    /// <param name="options">Explicit target permissions.</param>
    /// <param name="parent">An optional existing group parent for initial publication.</param>
    /// <param name="popupId">The nonreused child identity when parent is supplied.</param>
    /// <returns>A tracked reservation invisible to targets and job admission.</returns>
    public UiAutomationDiagnosticReplacement ReserveReplacement(string name, UiAutomationDiagnosticRegistration? current, UiSurface candidate, IUiAutomationHost candidateHost, UiAutomationTargetOptions? options = null, UiAutomationDiagnosticRegistration? parent = null, Guid? popupId = null)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidateHost);
        if (_stopping)
        {
            throw new InvalidOperationException("The runtime is stopping.");
        }

        if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Invalid target name.", nameof(name));
        }

        if (!Enabled)
        {
            var disabled = new UiAutomationDiagnosticRegistration(Guid.NewGuid(), static () => { }) { IsReleased = true };
            return new(disabled.TargetId, static (_, _) => ValueTask.CompletedTask, () => disabled, static _ => ValueTask.CompletedTask, static () => { }, static () => true);
        }

        Target? previous = current is null ? null : _targets.GetValueOrDefault(current.TargetId);
        if ((current is not null && (previous is null || !ReferenceEquals(previous.Registration, current) || !previous.Accepting || previous.Name != name))
            || _replacements.Values.Any(item => !item.Completed && ((item.Previous == previous && previous is not null) || item.Candidate.Name == name || ReferenceEquals(item.Candidate.Surface, candidate)))
            || _targets.Values.Any(target => target != previous && (target.Name == name || ReferenceEquals(target.Surface, candidate))))
        {
            throw new ArgumentException("Unknown current target or duplicate reservation.");
        }

        if (previous is null && _targets.Count + _replacements.Values.Count(item => item.Previous is null && !item.Completed) >= _options.MaximumTargets)
        {
            throw new InvalidOperationException("Diagnostic target capacity exceeded.");
        }

        Target? ancestor = parent is null ? null : _targets.GetValueOrDefault(parent.TargetId);
        if (parent is not null && (previous is not null || ancestor is null || !ReferenceEquals(ancestor.Registration, parent) || !ancestor.Accepting || popupId is null || popupId == Guid.Empty))
        {
            throw new ArgumentException("The parent must be a live target for an initial child reservation.", nameof(parent));
        }

        Target? permissionParent = previous?.ParentTargetId is { } parentId ? _targets.GetValueOrDefault(parentId) : ancestor;
        UiAutomationTargetOptions effective = options ?? new();
        if (permissionParent is { } owner)
        {
            if (candidateHost is not IUiAutomationRoutingHost || _targets[owner.ControlGroupId].Host is not IUiAutomationRoutingHost)
            {
                throw new ArgumentException("Child replacements require the shared routing host.", nameof(candidateHost));
            }

            effective = new()
            {
                AllowInput = owner.Options.AllowInput && effective.AllowInput,
                AllowTextInput = owner.Options.AllowTextInput && effective.AllowTextInput,
                AllowTextInspection = owner.Options.AllowTextInspection && effective.AllowTextInspection,
                AllowScreenshots = owner.Options.AllowScreenshots && effective.AllowScreenshots,
            };
        }

        Guid surfaceId = candidate.CaptureAutomationSnapshot().SurfaceId;
        ValueTask<Guid> identity = candidateHost.RunOnUiThreadAsync(surface => ReferenceEquals(surface, candidate) ? surface.CaptureAutomationSnapshot().SurfaceId : Guid.Empty);
        if (!identity.IsCompletedSuccessfully || identity.Result != surfaceId)
        {
            throw new ArgumentException("Reserve synchronously on the same surface owner.", nameof(candidateHost));
        }

        var id = Guid.NewGuid();
        var registration = new UiAutomationDiagnosticRegistration(id, () => Unregister(id));
        var target = new Target(id, name, candidate, candidateHost, effective, registration)
        {
            ParentTargetId = previous?.ParentTargetId ?? ancestor?.Id,
            PopupId = previous?.PopupId ?? popupId,
            ControlGroupId = previous?.ControlGroupId ?? ancestor?.ControlGroupId ?? id,
        };
        var replacement = new Replacement(this, previous, target);
        _targets.EnsureCapacity(_targets.Count + _replacements.Count + 1);
        _replacements.Add(id, replacement);
        return new(id, replacement.QuiesceAsync, replacement.Commit, replacement.DiscardAsync, () => _replacements.Remove(id), replacement.TryValidateCommit);
    }

    /// <summary>Captures only a live job executing on this registration's input control group.</summary>
    /// <param name="registration">A live registration in this runtime.</param>
    /// <returns>The trusted cause identity, or null for ordinary application input.</returns>
    public Guid? CaptureCurrentOperation(UiAutomationDiagnosticRegistration registration)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(registration);
        Job? job = _currentOperation.Value;
        return job is { Terminal: false } && _jobs.GetValueOrDefault(job.Id) == job && _targets.TryGetValue(job.TargetId, out Target? source)
            && _targets.TryGetValue(registration.TargetId, out Target? target) && ReferenceEquals(target.Registration, registration) && target.ControlGroupId == source.ControlGroupId ? job.Id : null;
    }

    private (bool Input, bool TextInput, bool TextInspection, bool Screenshots) TargetPermissions(Target target)
    {
        bool input = true;
        bool textInput = true;
        bool inspection = true;
        bool screenshots = true;
        Target current = target;
        while (true)
        {
            input &= current.Options.AllowInput;
            textInput &= current.Options.AllowTextInput;
            inspection &= current.Options.AllowTextInspection;
            screenshots &= current.Options.AllowScreenshots;
            if (current.ParentTargetId is not { } parent)
            {
                return (input, textInput, inspection, screenshots);
            }

            if (!_targets.TryGetValue(parent, out Target? ancestor))
            {
                return (false, false, false, false);
            }

            current = ancestor;
        }
    }

    private sealed class Replacement(UiAutomationDiagnosticsRuntime runtime, Target? previous, Target candidate)
    {
        private readonly TaskCompletionSource _quiescence = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;
        private bool _committed;
        private Guid? _origin;

        internal Target? Previous { get; private set; } = previous;

        internal Target Candidate { get; } = candidate;

        internal bool Completed { get; private set; }

        private Guid Group => Previous?.ControlGroupId ?? Candidate.ControlGroupId;

        internal ValueTask QuiesceAsync(Guid? originatingOperation, CancellationToken cancellationToken)
        {
            runtime.Verify();
            if (!_started)
            {
                if (originatingOperation is { } id && runtime._jobs.TryGetValue(id, out Job? cause) && !InGroup(cause))
                {
                    throw new ArgumentException("The operation is not a live job in this control group.", nameof(originatingOperation));
                }

                _started = true;
                _origin = originatingOperation is { } origin && runtime._jobs.TryGetValue(origin, out Job? operation) && !operation.Terminal ? origin : null;
                foreach (Target target in runtime._targets.Values.Where(target => target.ControlGroupId == Group))
                {
                    target.Paused = true;
                }

                foreach (Job job in runtime._jobs.Values.Where(job => !job.Terminal && job.Id != _origin && InGroup(job)))
                {
                    runtime.Cancel(job, "navigation-quiesce");
                }

                Advance();
            }

            return new(_quiescence.Task.WaitAsync(cancellationToken));
        }

        internal void Advance()
        {
            if (_started && !Completed && !runtime._jobs.Values.Any(job => !job.Terminal && InGroup(job)))
            {
                if (runtime._targets.TryGetValue(Group, out Target? group) && group.Control is { } control)
                {
                    runtime.ReleaseControl(control, "navigation-quiesce");
                }

                _quiescence.TrySetResult();
            }
        }

        internal bool TryValidateCommit() => !runtime._stopping && !Completed && _quiescence.Task.IsCompletedSuccessfully
            && (Previous is null || (runtime._targets.GetValueOrDefault(Previous.Id) == Previous && Previous.Accepting))
            && (Candidate.ParentTargetId is not { } parent || (runtime._targets.TryGetValue(parent, out Target? target) && target.Accepting));

        internal UiAutomationDiagnosticRegistration Commit()
        {
            runtime.Verify();
            if (!TryValidateCommit())
            {
                throw new InvalidOperationException("Quiesce the reservation before publication.");
            }

            Guid oldGroup = Group;
            if (Previous is { } old)
            {
                runtime._targets.Remove(old.Id);
                old.Registration.IsReleased = true;
                if (old.Control is { } control)
                {
                    runtime._controls.Remove(control.Id);
                }

                if (old.ControlGroupId == old.Id)
                {
                    Candidate.ControlGroupId = Candidate.Id;
                }

                foreach (Target target in runtime._targets.Values)
                {
                    if (old.ControlGroupId == old.Id && target.ControlGroupId == old.Id)
                    {
                        target.ControlGroupId = Candidate.Id;
                    }

                    if (target.ParentTargetId == old.Id)
                    {
                        target.ParentTargetId = Candidate.Id;
                    }
                }

                foreach (Replacement reservation in runtime._replacements.Values)
                {
                    if (old.ControlGroupId == old.Id && reservation.Candidate.ControlGroupId == old.Id)
                    {
                        reservation.Candidate.ControlGroupId = Candidate.Id;
                    }

                    if (reservation.Candidate.ParentTargetId == old.Id)
                    {
                        reservation.Candidate.ParentTargetId = Candidate.Id;
                    }
                }
            }

            runtime._targets.Add(Candidate.Id, Candidate);
            Completed = true;
            foreach (Target target in runtime._targets.Values)
            {
                if (target.ControlGroupId == oldGroup || target.ControlGroupId == Candidate.ControlGroupId)
                {
                    bool paused = false;
                    foreach (Replacement reservation in runtime._replacements.Values)
                    {
                        paused |= reservation._started && !reservation.Completed && reservation.Group == target.ControlGroupId;
                    }

                    target.Paused = paused;
                }
            }

            Previous = null;
            _committed = true;
            Completed = true;
            return Candidate.Registration;
        }

        internal async ValueTask DiscardAsync(CancellationToken cancellationToken)
        {
            runtime.Verify();
            if (_committed)
            {
                throw new InvalidOperationException("A committed reservation cannot be discarded.");
            }

            if (_started)
            {
                await _quiescence.Task.WaitAsync(cancellationToken);
            }

            Completed = true;
            if (!runtime._stopping)
            {
                foreach (Target target in runtime._targets.Values)
                {
                    if (target.ControlGroupId == Group && target.Accepting)
                    {
                        bool paused = false;
                        foreach (Replacement reservation in runtime._replacements.Values)
                        {
                            paused |= reservation._started && !reservation.Completed && reservation.Group == target.ControlGroupId;
                        }

                        target.Paused = paused;
                    }
                }
            }
        }

        private bool InGroup(Job job) => runtime._targets.TryGetValue(job.TargetId, out Target? target) && target.ControlGroupId == Group;
    }
}
