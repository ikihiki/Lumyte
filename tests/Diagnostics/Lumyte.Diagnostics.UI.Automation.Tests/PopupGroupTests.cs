using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.SampleSupport;
using Xunit;
using static Lumyte.Diagnostics.UI.Automation.Tests.Harness;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Verifies PopupGroupTests.</summary>
public sealed class PopupGroupTests
{
    /// <summary>Checks ParentAndPopupShareControlSequenceAndCannotReplayAcrossTargets.</summary>
    [Fact]
    public void ParentAndPopupShareControlSequenceAndCannotReplayAcrossTargets()
    {
        using var h = new Harness();
        using Lumyte.UI.UiSurface surface = UiAutomationExercise.CreateSurface(() =>
        {
        });
        surface.SetViewport(new(240, 160));
        var host = new UiSampleAutomationHost(surface, _ => ValueTask.CompletedTask, h.Clock);
        UiAutomationDiagnosticRegistration child = h.Runtime.RegisterPopup(h.Registration, Guid.NewGuid(), "popup", surface, Route(host), new() { AllowInput = true, });
        Guid control = h.Acquire();
        Dictionary<string, DiagnosticValue> acquire = Args(("target-id", child.TargetId.ToString("D")), ("duration-ms", 5000L));
        Assert.Equal("busy", h.Call("control-acquire", acquire, actor: "other").Code);
        Assert.Equal(control, Guid.Parse(Success(h.Call("control-acquire", acquire))["control-id"].String!));
        Guid job = Job(h.Start(Plan("click"), control));
        Assert.Equal("Succeeded", h.Complete(job, true)["state"].String);
        Dictionary<string, DiagnosticValue> action = Args(("target-id", child.TargetId.ToString("D")), ("control-id", control.ToString("D")), ("sequence", 1L), ("plan-json", UiRemoteSchema.SerializePlan(Plan("click"))), ("job-timeout-ms", 1000L));
        Assert.Equal("action-id-conflict", h.Call("action-start", action).Code);
        UiRemoteTarget[] targets = JsonSerializer.Deserialize(Success(h.Call("targets"))["json"].String!, UiAutomationJsonContext.Default.UiRemoteTargetArray)!;
        Assert.Equal(h.Registration.TargetId, targets.Single(t => t.Id == child.TargetId).ControlGroupId);
        child.Dispose();
        Assert.True(child.IsReleased);
        Assert.False(h.Registration.IsReleased);
        Assert.True(host.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>Checks ParentReleaseDrainsChildRegistrationsBeforeBorrowedSurfacesAreReturned.</summary>
    [Fact]
    public void ParentReleaseDrainsChildRegistrationsBeforeBorrowedSurfacesAreReturned()
    {
        using var h = new Harness();
        using Lumyte.UI.UiSurface surface = UiAutomationExercise.CreateSurface(() =>
        {
        });
        surface.SetViewport(new(240, 160));
        var host = new UiSampleAutomationHost(surface, _ => ValueTask.CompletedTask, h.Clock);
        UiAutomationDiagnosticRegistration child = h.Runtime.RegisterPopup(h.Registration, Guid.NewGuid(), "popup", surface, Route(host));
        h.Registration.Dispose();
        h.Step();
        Assert.True(child.IsReleased);
        Assert.True(h.Registration.IsReleased);
        Assert.True(host.DisposeAsync().IsCompletedSuccessfully);
    }
}
