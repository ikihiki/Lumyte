using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns bounded UI jobs and detached results on one diagnostic execution owner.</summary>
public sealed partial class UiAutomationDiagnosticsRuntime
{
    internal DiagnosticResult<UiAutomationTargetsReceipt> Targets()
    {
        Verify();
        UiRemoteTarget[] targets = _targets.Values.Where(target => target.Accepting).Select(target => new UiRemoteTarget(target.Id, target.Name, target.Options.AllowInput, target.Options.AllowScreenshots, target.ParentTargetId, target.PopupId, target.ControlGroupId, target.AutomationCode, target.Options.AllowTextInput, target.Options.AllowTextInspection)).ToArray();
        string json = JsonSerializer.Serialize(targets, UiAutomationJsonContext.Default.UiRemoteTargetArray);
        return json.Length <= 32768 ? DiagnosticResult<UiAutomationTargetsReceipt>.Success(new(json)) : Reject<UiAutomationTargetsReceipt>("result-too-large");
    }

    internal DiagnosticResult<UiAutomationControlReceipt> Acquire(DiagnosticOperationContext context, string targetId, long durationMs)
    {
        Verify();
        if (!CanAdmit(context) || !_targets.TryGetValue(Id(targetId), out Target? target) || !target.Accepting)
        {
            return Reject<UiAutomationControlReceipt>("not-found");
        }

        if (!target.Options.AllowInput || durationMs is < 1000 or > 5000)
        {
            return Reject<UiAutomationControlReceipt>("input-not-allowed");
        }

        target = _targets[target.ControlGroupId];
        if (target.Control is { } current && current.State != "Released")
        {
            if (current.State == "Active" && Remaining(current.Started, current.Duration) <= TimeSpan.Zero)
            {
                ReleaseControl(current, "lease-expired");
            }

            return current.Session == context.SessionId && current.Actor == context.ActorId && current.State == "Active"
                ? ControlReceipt(current) : Reject<UiAutomationControlReceipt>("busy");
        }

        if (target.Control is not null)
        {
            _controls.Remove(target.Control.Id);
        }

        var control = new Control(target.Id, context.SessionId, context.ActorId, _clock.GetTimestamp(), TimeSpan.FromMilliseconds(durationMs));
        _controls.Add(control.Id, control);
        target.Control = control;
        AuditControl("acquire", control);
        return ControlReceipt(control);
    }

    internal DiagnosticResult<UiAutomationControlReceipt> GetControl(DiagnosticOperationContext context, string controlId)
    {
        Verify();
        Control? control = FindControl(context, controlId);
        return control is null ? Reject<UiAutomationControlReceipt>("not-found") : ControlReceipt(control);
    }

    internal DiagnosticResult<UiAutomationControlReceipt> ChangeControl(DiagnosticOperationContext context, string controlId, long? durationMs)
    {
        Verify();
        Control? control = FindControl(context, controlId);
        if (control is null)
        {
            return Reject<UiAutomationControlReceipt>("not-found");
        }

        if (context.ExpectedRevision != control.Revision)
        {
            return DiagnosticResult<UiAutomationControlReceipt>.Conflict(control.Revision);
        }

        if (durationMs is null)
        {
            ReleaseControl(control, "control-released");
            Settle();
        }
        else
        {
            if (!CanAdmit(context) || control.State != "Active" || Remaining(control.Started, control.Duration) <= TimeSpan.Zero)
            {
                return Reject<UiAutomationControlReceipt>("lease-gone");
            }

            if (durationMs is < 1000 or > 5000)
            {
                return Reject<UiAutomationControlReceipt>("invalid-duration");
            }

            control.Started = _clock.GetTimestamp();
            control.Duration = TimeSpan.FromMilliseconds(durationMs.Value);
            control.Revision++;
            AuditControl("renew", control);
        }

        return ControlReceipt(control);
    }

    internal DiagnosticResult<UiAutomationJobReceipt> Start(DiagnosticOperationContext context, string targetId, string? controlId, long sequence, string planJson, long jobTimeoutMs)
    {
        Verify();
        if (!CanAdmit(context) || !_targets.TryGetValue(Id(targetId), out Target? target) || !target.Accepting)
        {
            return Reject<UiAutomationJobReceipt>("not-found");
        }

        UiRemotePlan plan;
        try
        {
            plan = UiRemoteSchema.ParsePlan(planJson);
        }
        catch (JsonException)
        {
            return Reject<UiAutomationJobReceipt>("invalid-plan");
        }

        bool action = controlId is not null;
        if (UiRemoteSchema.IsAction(plan) != action || jobTimeoutMs is < 100 or > 10000)
        {
            return Reject<UiAutomationJobReceipt>("invalid-plan");
        }

        Control? control = action ? FindControl(context, controlId!) : null;
        if (action && (!target.Options.AllowInput || control is null || control.TargetId != target.ControlGroupId || control.State != "Active" || Remaining(control.Started, control.Duration) <= TimeSpan.Zero))
        {
            return Reject<UiAutomationJobReceipt>("lease-gone");
        }

        if (plan.Kind == "screenshot" && !target.Options.AllowScreenshots)
        {
            return Reject<UiAutomationJobReceipt>("screenshot-not-allowed");
        }

        if (plan.Kind is "fill" or "ime-select" or "ime-page" or "ime-finalize" or "ime-abort" && !target.Options.AllowTextInput)
        {
            return Reject<UiAutomationJobReceipt>("text-input-disabled");
        }

        if (plan.Kind is "expect-value" or "ime-candidates" && !target.Options.AllowTextInspection)
        {
            return Reject<UiAutomationJobReceipt>("text-inspection-disabled");
        }

        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.Id.ToString("D") + "|" + UiRemoteSchema.SerializePlan(plan) + "|" + jobTimeoutMs.ToString(CultureInfo.InvariantCulture))));
        if (control is not null)
        {
            if (sequence < control.NextSequence)
            {
                Job? previous = _jobs.Values.FirstOrDefault(job => job.ControlId == control.Id && job.Sequence == sequence);
                if (previous is not null && Expired(previous))
                {
                    Forget(previous);
                    previous = null;
                }

                return previous is null ? Reject<UiAutomationJobReceipt>("action-forgotten")
                    : previous.Fingerprint == fingerprint ? JobReceipt(previous) : Reject<UiAutomationJobReceipt>("action-id-conflict");
            }

            if (sequence != control.NextSequence || sequence == long.MaxValue)
            {
                return Reject<UiAutomationJobReceipt>("sequence-gap");
            }
        }

        if (_jobs.Count >= _options.MaximumJobs || (action && _jobs.Values.Any(job => job.ControlId == control!.Id && !job.Terminal))
            || (plan.Kind == "screenshot" && _jobs.Values.Any(job => job.TargetId == target.Id && job.Plan?.Kind == "screenshot" && !job.Terminal)))
        {
            return Reject<UiAutomationJobReceipt>("busy");
        }

        var added = new Job(target.Id, context.SessionId, context.ActorId, control?.Id, sequence, fingerprint, plan, _clock.GetTimestamp(), TimeSpan.FromMilliseconds(jobTimeoutMs), target.Host) { RegisteredSurface = target.Surface };
        _jobs.Add(added.Id, added);
        if (control is not null)
        {
            control.NextSequence++;
            control.Revision++;
        }

        Audit("accepted", added);
        return JobReceipt(added);
    }

    internal DiagnosticResult<UiAutomationJobReceipt> ReadJob(DiagnosticOperationContext context, string jobId, bool action, string operation)
    {
        Verify();
        if (!_jobs.TryGetValue(Id(jobId), out Job? job) || job.Session != context.SessionId || job.Actor != context.ActorId || (job.ControlId is not null) != action)
        {
            return Reject<UiAutomationJobReceipt>("not-found");
        }

        if (Expired(job))
        {
            Forget(job);
            return Reject<UiAutomationJobReceipt>("not-found");
        }

        if (operation == "cancel")
        {
            Cancel(job, "caller-canceled");
        }
        else if (operation == "forget")
        {
            if (!job.Terminal)
            {
                return Reject<UiAutomationJobReceipt>("busy");
            }

            DiagnosticResult<UiAutomationJobReceipt> receipt = JobReceipt(job);
            Forget(job);
            return receipt;
        }

        return JobReceipt(job);
    }

    internal DiagnosticResult<UiAutomationArtifactReceipt> ReadArtifact(DiagnosticOperationContext context, string jobId, string artifactId, long offset, long count)
    {
        Verify();
        if (!_jobs.TryGetValue(Id(jobId), out Job? job) || job.Session != context.SessionId || job.Actor != context.ActorId
            || !_artifacts.TryGetValue(Id(artifactId), out Artifact? artifact) || artifact.JobId != job.Id)
        {
            return Reject<UiAutomationArtifactReceipt>("not-found");
        }

        if (Expired(job))
        {
            Forget(job);
            return Reject<UiAutomationArtifactReceipt>("not-found");
        }

        if (offset < 0 || offset > artifact.Bytes.LongLength || count is < 1 or > 65536)
        {
            return Reject<UiAutomationArtifactReceipt>("invalid-range");
        }

        int length = (int)Math.Min(count, artifact.Bytes.LongLength - offset);
        return DiagnosticResult<UiAutomationArtifactReceipt>.Success(new(artifact.Id.ToString("D"), artifact.ContentType, artifact.Bytes.LongLength, artifact.Hash, offset, length, Convert.ToBase64String(artifact.Bytes, (int)offset, length)));
    }

    private static Guid Id(string value) => Guid.TryParseExact(value, "D", out Guid id) ? id : Guid.Empty;

    private static DiagnosticResult<T> Reject<T>(string code)
        where T : notnull => DiagnosticResult<T>.Reject(code, "The UI automation request was rejected.");

    private static DiagnosticResult<UiAutomationJobReceipt> JobReceipt(Job job) => DiagnosticResult<UiAutomationJobReceipt>.Success(new(job.Id.ToString("D"), job.State, job.Code, job.InputMayHaveBeenSent, job.ResultId?.ToString("D") ?? string.Empty, job.TraceId?.ToString("D") ?? string.Empty, job.Revision), job.Revision);

    private bool Expired(Job job) => job.Terminal && _clock.GetElapsedTime(job.Completed) >= _options.Retention;

    private bool CanAdmit(DiagnosticOperationContext context) => Enabled && !_stopping && !_closedSessions.ContainsKey(context.SessionId) && Volatile.Read(ref _closeOverflow) == 0;

    private Control? FindControl(DiagnosticOperationContext context, string id) => _controls.TryGetValue(Id(id), out Control? control) && control.Session == context.SessionId && control.Actor == context.ActorId ? control : null;

    private DiagnosticResult<UiAutomationControlReceipt> ControlReceipt(Control control) => DiagnosticResult<UiAutomationControlReceipt>.Success(new(control.Id.ToString("D"), control.State, Math.Max(0, (long)Remaining(control.Started, control.Duration).TotalMilliseconds), control.Revision, control.NextSequence), control.Revision);
}
