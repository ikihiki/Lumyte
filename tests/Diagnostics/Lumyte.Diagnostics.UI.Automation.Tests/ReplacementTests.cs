using System.Numerics;
using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Lumyte.UI.Automation;
using Xunit;
using static Lumyte.Diagnostics.UI.Automation.Tests.Harness;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks a staged child cannot gain permissions denied by its parent.</summary>
public sealed class ReplacementTests
{
    /// <summary>Checks parent policy applies to both initial child publication and subsequent replacement.</summary>
    /// <param name="replace">Whether the child already has a live registration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementPreservesParentPermissionBounds(bool replace)
    {
        using var parentSurface = new UiSurface(Panel());
        using var childSurface = new UiSurface(Panel());
        using var candidate = new UiSurface(Panel());
        using var harness = new Harness();
        UiAutomationDiagnosticRegistration parent = harness.Runtime.Register("parent", parentSurface, new Host(parentSurface));
        UiAutomationTargetOptions requested = new() { AllowInput = true, AllowTextInput = true, AllowTextInspection = true, AllowScreenshots = true };
        UiAutomationDiagnosticRegistration? current = replace ? harness.Runtime.RegisterPopup(parent, Guid.NewGuid(), "child", childSurface, new Host(childSurface), requested) : null;
        UiAutomationDiagnosticReplacement replacement = harness.Runtime.ReserveReplacement("child", current, candidate, new Host(candidate), requested, replace ? null : parent, replace ? null : Guid.NewGuid());
        ValueTask quiesce = replacement.QuiesceAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(quiesce.IsCompletedSuccessfully);
        Assert.True(replacement.TryValidateCommit());
        UiAutomationDiagnosticRegistration published = replacement.Commit();
        Assert.Equal("input-not-allowed", harness.Call("control-acquire", Args(("target-id", published.TargetId.ToString("D")), ("duration-ms", 5000L))).Code);
        Assert.Equal("screenshot-not-allowed", harness.Call("observe-start", Args(("target-id", published.TargetId.ToString("D")), ("plan-json", UiRemoteSchema.SerializePlan(new(1, "screenshot", []))), ("job-timeout-ms", 1000L))).Code);
        Assert.True(replacement.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>Checks a hidden reservation is retained through stop and cannot republish a released old target.</summary>
    [Fact]
    public void RuntimeStopWaitsForHiddenReservationsToBeDiscarded()
    {
        using var candidate = new UiSurface(Panel());
        using var harness = new Harness();
        UiAutomationDiagnosticReplacement replacement = harness.Runtime.ReserveReplacement("settings", harness.Registration, candidate, new Host(candidate));
        harness.Runtime.BeginStop();
        Assert.False(harness.Runtime.IsStopped);
        Assert.False(replacement.TryValidateCommit());
        Assert.True(replacement.DiscardAsync(CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(replacement.DisposeAsync().IsCompletedSuccessfully);
        Assert.True(harness.Runtime.IsStopped);
    }

    /// <summary>Checks replacing a non-root parent reparents its children and immediately narrows their effective permissions.</summary>
    [Fact]
    public void ParentReplacementReparentsAndRestrictsExistingChildren()
    {
        using var parentSurface = new UiSurface(Panel());
        using var childSurface = new UiSurface(Panel());
        using var candidate = new UiSurface(Panel());
        using var harness = new Harness(screenshots: true);
        var allowed = new UiAutomationTargetOptions { AllowInput = true, AllowScreenshots = true };
        UiAutomationDiagnosticRegistration parent = harness.Runtime.RegisterPopup(harness.Registration, Guid.NewGuid(), "parent", parentSurface, new Host(parentSurface), allowed);
        UiAutomationDiagnosticRegistration child = harness.Runtime.RegisterPopup(parent, Guid.NewGuid(), "child", childSurface, new Host(childSurface), allowed);
        UiAutomationDiagnosticReplacement replacement = harness.Runtime.ReserveReplacement("parent", parent, candidate, new Host(candidate));
        Assert.True(replacement.QuiesceAsync(null, CancellationToken.None).IsCompletedSuccessfully);
        UiAutomationDiagnosticRegistration published = replacement.Commit();
        Assert.Equal("input-not-allowed", harness.Call("control-acquire", Args(("target-id", child.TargetId.ToString("D")), ("duration-ms", 5000L))).Code);
        using var document = JsonDocument.Parse(Success(harness.Call("targets"))["json"].String!);
        JsonElement observation = document.RootElement.EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == child.TargetId);
        Assert.Equal(published.TargetId, observation.GetProperty("parentTargetId").GetGuid());
        Assert.False(observation.GetProperty("allowInput").GetBoolean());
        Assert.False(observation.GetProperty("allowScreenshots").GetBoolean());
        Assert.True(replacement.DisposeAsync().IsCompletedSuccessfully);
    }

    private sealed class Host(UiSurface surface) : IUiAutomationHost, IUiAutomationRoutingHost
    {
        public TimeProvider TimeProvider => TimeProvider.System;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => new(action(surface));

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public UiPointerProbe ProbePointer(UiSurface source, Vector2 position) => source.ProbePointer(position);

        public bool CanReceiveKeys(UiSurface source) => true;

        public bool Dispatch(UiSurface source, UiInput input) => source.Dispatch(input);
    }
}
