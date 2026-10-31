namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Generated synchronous admission and detached result operations; asynchronous UI work runs only in Advance.</summary>
public sealed partial class UiAutomationDiagnostics(UiAutomationDiagnosticsRuntime runtime)
{
    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "targets", Traffic = DiagnosticOperationTraffic.Standard)]
    private DiagnosticResult<UiAutomationTargetsReceipt> Targets() => runtime.Targets();

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-acquire", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationControlReceipt> Acquire(DiagnosticOperationContext context, string targetId, [DiagnosticArgument(Minimum = 1000, Maximum = 5000)] long durationMs) => runtime.Acquire(context, targetId, durationMs);

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-get", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationControlReceipt> GetControl(DiagnosticOperationContext context, string controlId) => runtime.GetControl(context, controlId);

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-renew", Traffic = DiagnosticOperationTraffic.Control, RequiresRevision = true)]
    private DiagnosticResult<UiAutomationControlReceipt> Renew(DiagnosticOperationContext context, string controlId, [DiagnosticArgument(Minimum = 1000, Maximum = 5000)] long durationMs) => runtime.ChangeControl(context, controlId, durationMs);

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-release", Traffic = DiagnosticOperationTraffic.Control, RequiresRevision = true)]
    private DiagnosticResult<UiAutomationControlReceipt> Release(DiagnosticOperationContext context, string controlId) => runtime.ChangeControl(context, controlId, null);

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-start", Traffic = DiagnosticOperationTraffic.Standard)]
    private DiagnosticResult<UiAutomationJobReceipt> ActionStart(DiagnosticOperationContext context, string targetId, string controlId, long sequence, [DiagnosticArgument(MaxLength = 32768)] string planJson, [DiagnosticArgument(Minimum = 100, Maximum = 10000)] long jobTimeoutMs) => runtime.Start(context, targetId, controlId, sequence, planJson, jobTimeoutMs);

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-get", Traffic = DiagnosticOperationTraffic.Stream)]
    private DiagnosticResult<UiAutomationJobReceipt> ActionGet(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, true, "get");

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-cancel", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationJobReceipt> ActionCancel(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, true, "cancel");

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-forget", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationJobReceipt> ActionForget(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, true, "forget");

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-start", Traffic = DiagnosticOperationTraffic.Standard)]
    private DiagnosticResult<UiAutomationJobReceipt> ObserveStart(DiagnosticOperationContext context, string targetId, [DiagnosticArgument(MaxLength = 32768)] string planJson, [DiagnosticArgument(Minimum = 100, Maximum = 10000)] long jobTimeoutMs) => runtime.Start(context, targetId, null, 0, planJson, jobTimeoutMs);

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-get", Traffic = DiagnosticOperationTraffic.Stream)]
    private DiagnosticResult<UiAutomationJobReceipt> ObserveGet(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, false, "get");

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-cancel", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationJobReceipt> ObserveCancel(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, false, "cancel");

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-forget", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<UiAutomationJobReceipt> ObserveForget(DiagnosticOperationContext context, string jobId) => runtime.ReadJob(context, jobId, false, "forget");

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "artifact-read", Traffic = DiagnosticOperationTraffic.Standard)]
    private DiagnosticResult<UiAutomationArtifactReceipt> ReadArtifact(DiagnosticOperationContext context, string jobId, string artifactId, [DiagnosticArgument(Minimum = 0)] long offset, [DiagnosticArgument(Minimum = 1, Maximum = 65536)] long count) => runtime.ReadArtifact(context, jobId, artifactId, offset, count);
}
