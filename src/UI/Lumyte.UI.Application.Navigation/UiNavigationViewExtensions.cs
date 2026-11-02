using System.Runtime.CompilerServices;
using Lumyte.UI.Navigation;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Adapts a standard application's staged view exchange to optional navigation.</summary>
public static class UiNavigationViewExtensions
{
    private static readonly ConditionalWeakTable<UiApplicationViewSlot, NavigationHost> _hosts = new();

    /// <summary>Reserves the slot for navigation and returns its stable borrowed host.</summary>
    /// <param name="slot">The window's view slot.</param>
    /// <returns>The navigation host managed by the application.</returns>
    public static IUiNavigationHost CreateNavigationHost(this UiApplicationViewSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        slot.Context.CheckOwner();
        return _hosts.GetValue(slot, static value => new(value.CreateViewHost()));
    }

    /// <summary>Copies committed metadata, excluding any unpublished candidate.</summary>
    /// <param name="slot">A slot reserved for navigation.</param>
    /// <returns>The committed view snapshot.</returns>
    public static UiNavigationWindowSnapshot CaptureNavigationSnapshot(this UiApplicationViewSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        (ulong Revision, object? Metadata, Guid SurfaceId, Guid? TargetId) view = slot.CaptureViewSnapshot();
        if (!IsConfigured(slot))
        {
            throw new InvalidOperationException("Navigation is not configured for this slot.");
        }

        UiNavigationCommittedView? current = view.Metadata is UiNavigationPublication publication ? new(publication.NavigatorId, publication.EntryId, publication.ActivationId, publication.Route, view.SurfaceId, view.TargetId) : null;
        return new(slot.Context.Id, slot.Context.Window, view.Revision, current);
    }

    internal static bool IsConfigured(UiApplicationViewSlot slot) => _hosts.TryGetValue(slot, out _);

    private sealed class NavigationHost(IUiApplicationViewHost host) : IUiNavigationHost
    {
        public UiWindowId Window => host.Window;

        public IUiDispatcher Dispatcher => host.Dispatcher;

        public Guid? CaptureInputOperation() => host.CaptureInputOperation();

        public IUiNavigationViewChange BeginChange(Guid activationId) => new Change(host.BeginChange());
    }

    private sealed class Change(IUiApplicationViewChange change) : IUiNavigationViewChange, IUiNavigationVisualChange
    {
        public ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken) => change.PrepareAsync(root, cancellationToken);

        public ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken) => change.QuiesceAsync(originatingInputOperation, cancellationToken);

        public ValueTask PrepareVisualsAsync(UiPageTransitions? transitions, Func<bool> skipRequested, CancellationToken cancellationToken)
        {
            UiViewVisualPlan? plan = transitions is null ? null : new(transitions.Enter, transitions.Exit, transitions.Order == UiPageTransitionOrder.ExitThenEnter, transitions.ConnectSharedElements, transitions.ConnectedDuration, transitions.ConnectedEasing);
            return ((IUiApplicationVisualChange)change).PrepareVisualsAsync(plan, skipRequested, cancellationToken);
        }

        public async ValueTask<UiVisualTransitionResult> RunVisualsAsync()
        {
            UiViewVisualOutcome outcome = await ((IUiApplicationVisualChange)change).RunVisualsAsync();
            return new(outcome.Interrupted ? UiVisualTransitionStatus.Interrupted : outcome.Played ? UiVisualTransitionStatus.Completed : UiVisualTransitionStatus.Skipped, outcome.Code);
        }

        public void StopVisuals() => ((IUiApplicationVisualChange)change).StopVisuals();

        public bool TryValidateCommit(out string code) => change.TryValidateCommit(out code);

        public void Commit(UiNavigationPublication publication) => change.Commit(publication);

        public ValueTask DrainRetiredAsync(CancellationToken cancellationToken) => change.DrainRetiredAsync(cancellationToken);

        public ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken) => change.ReleaseRetiredAsync(cancellationToken);

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => change.DrainCandidateAsync(cancellationToken);

        public ValueTask RunPageCleanupAsync(Func<CancellationToken, ValueTask> cleanup, CancellationToken cancellationToken) => change.RunContentCleanupAsync(cleanup, cancellationToken);

        public ValueTask DiscardAsync(CancellationToken cancellationToken) => change.DiscardAsync(cancellationToken);

        public ValueTask DisposeAsync() => change.DisposeAsync();
    }
}
