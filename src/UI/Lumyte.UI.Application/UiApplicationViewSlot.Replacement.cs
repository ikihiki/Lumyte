using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Coordinates staged publication and retained cleanup on the UI owner.</summary>
public sealed partial class UiApplicationViewSlot
{
    private sealed class ViewHost(UiApplicationViewSlot slot) : IUiApplicationViewHost
    {
        private Change? _preparing;

        public UiWindowId Window => slot._window;

        public IUiDispatcher Dispatcher => slot._application.Dispatcher;

        public Guid? CaptureInputOperation() => slot._application.ViewParticipants.Select(participant => participant.CaptureInputOperation(slot.Context)).FirstOrDefault(id => id is not null);

        public IUiApplicationViewChange BeginChange()
        {
            slot._application.Owner.CheckOwner();
            if (slot._stopping || _preparing is not null)
            {
                throw new InvalidOperationException("The slot is stopping or already preparing a change.");
            }

            _preparing = new(slot, this);
            return _preparing;
        }

        private sealed class Change(UiApplicationViewSlot slot, ViewHost host) : IUiApplicationViewChange, IUiApplicationVisualChange
        {
            private readonly List<IUiApplicationViewDiagnosticChange> _diagnostics = [];
            private View? _previous = slot._current;
            private View? _candidate;
            private IUiWindowPresentationReplacement? _replacement;
            private UiPopupManager.PreparedOwner? _owner;
            private Action? _inputCommit;
            private Widget? _root;
            private object? _inputVersion;
            private UiSignalRevision _signalRevision;
            private UiFocusBookmark? _bookmark;
            private ulong _graphicsGeneration;
            private Guid _surfaceId;
            private ChangePhase _phase;
            private bool _gated;
            private Task? _cleanup;
            private UiPageVisualFlight? _visual;

            private enum ChangePhase
            {
                Preparing,
                Quiesced,
                Committed,
                Released,
                Discarded,
                Disposed,
            }

            public async ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken)
            {
                slot._application.Owner.CheckOwner();
                slot._application.ValidateEditors(root);
                _candidate = new(slot._application, slot._window, root) { Suspended = true };
                _candidate.Visuals.SuspendPublication();
                _candidate.Host.Pause();
                _candidate.Surface.SetSignalUpdatesEnabled(false);
                _candidate.Surface.SetAnimationUpdatesEnabled(false);
                _graphicsGeneration = slot._application.GraphicsGeneration;
                _root = root;
                _replacement = ((IUiWindowPresentationReplacementManager)slot._application.Windows).BeginReplacement(slot._window, _previous?.Binding, _candidate.Surface);
                await _replacement.PrepareAsync(cancellationToken);
                _candidate.AttachText(true);
                _owner = slot._application.Popups.PrepareOwnerReplacement(_previous?.Owner, slot._window, _candidate.Surface, _candidate.Text is { } text ? text.Connection.SuspendAsync : null);
                _candidate.Owner = _owner.Token;
                _candidate.AttachPortals();
                _candidate.Portals!.SetUpdatesEnabled(false);
                _surfaceId = _candidate.Surface.CaptureAutomationSnapshot().SurfaceId;
                _inputVersion = _candidate.Surface.InputVersionForWindowing;
                _signalRevision = _candidate.Surface.CommittedSignalRevision;
                _inputCommit = slot._application.PrepareInputReplacement(slot, _previous?.Surface, _candidate.Surface);
                foreach (IUiApplicationViewParticipant participant in slot._application.ViewParticipants)
                {
                    if (participant.ReserveViewReplacement(slot.Context, _candidate.Surface, _candidate.GetAutomation(true)) is { } change)
                    {
                        _diagnostics.Add(change);
                    }
                }
            }

            public async ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken)
            {
                foreach (IUiApplicationViewDiagnosticChange change in _diagnostics)
                {
                    await change.QuiesceAsync(originatingInputOperation, cancellationToken);
                }

                _previous?.Host.Pause();
                _previous?.Portals?.SetUpdatesEnabled(false);
                while (_previous?.Host.HasLease == true)
                {
                    await slot._application.NextTurnAsync().AsTask().WaitAsync(cancellationToken);
                }

                if (_previous is { } previous)
                {
                    previous.Suspended = true;
                    previous.Surface.SetSignalUpdatesEnabled(false);
                    previous.Surface.SetAnimationUpdatesEnabled(false);
                    _bookmark = previous.Surface.CaptureFocus();
                    _gated = true;
                    slot._application.Popups.GateOwner(previous.Owner!, true);
                    slot._application.QuiesceInput(previous.Surface);
                    previous.Text?.Pause();
                    previous.Surface.Dispatch(new UiFocusLostInput());
                    while (!slot._application.Popups.IsOwnerQuiescent(previous.Owner!) || previous.Text?.Paused == false)
                    {
                        await slot._application.NextTurnAsync().AsTask().WaitAsync(cancellationToken);
                    }
                }

                _phase = ChangePhase.Quiesced;
            }

            public async ValueTask PrepareVisualsAsync(UiViewVisualPlan? plan, Func<bool> skip, CancellationToken token)
            {
                _visual = new(slot._application, _candidate!.Visuals, _previous?.Visuals, plan, skip);
                _candidate.Visuals.Page = _visual;
                await _visual.PrepareAsync(token);
            }

            public ValueTask<UiViewVisualOutcome> RunVisualsAsync() => new(_visual!.Completion);

            public void StopVisuals() => _visual?.Stop();

            public bool TryValidateCommit(out string code)
            {
                code = "candidate-stale";
                return !slot._stopping && _phase == ChangePhase.Quiesced && _candidate is not null && ReferenceEquals(slot._current, _previous) && ReferenceEquals(_candidate.Surface.Root, _root) && ReferenceEquals(_candidate.Surface.InputVersionForWindowing, _inputVersion)
                    && _candidate.Surface.SignalRevision == _signalRevision && _diagnostics.All(change => change.IsReady) && slot._application.State != UiApplicationState.Recovering && slot._application.GraphicsGeneration == _graphicsGeneration && _replacement!.TryValidateCommit(out code);
            }

            public void Commit(object metadata)
            {
                _candidate!.Binding = _replacement!.Commit();
                _visual?.Commit();
                _candidate.Visuals.Publish();
                _owner!.Commit();
                _inputCommit!();
                _inputCommit = null;
                _bookmark = null;
                foreach (IUiApplicationViewDiagnosticChange change in _diagnostics)
                {
                    change.Commit();
                }

                _candidate.Suspended = false;
                slot._current = _candidate;
                _candidate.Gated = true;
                slot._publication = (metadata, _surfaceId, _diagnostics.Count == 0 ? null : _diagnostics[0].TargetId);
                slot._revision++;
                _phase = ChangePhase.Committed;
                host._preparing = null;
            }

            public async ValueTask DrainRetiredAsync(CancellationToken cancellationToken)
            {
                if (_visual is not null)
                {
                    await _visual.DrainAsync();
                }

                if (_previous is { } previous)
                {
                    previous.BeginStop(false);
                    while (!previous.IsStopped)
                    {
                        await slot._application.NextTurnAsync().AsTask().WaitAsync(cancellationToken);
                    }
                }

                await _replacement!.DrainPreviousAsync(cancellationToken);
            }

            public async ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken)
            {
                await _replacement!.ReleasePreviousAsync(cancellationToken);
                if (_previous is not null)
                {
                    await _previous.ReleaseTransferredAsync();
                    _previous = null;
                }

                _phase = ChangePhase.Released;
            }

            public async ValueTask DrainCandidateAsync(CancellationToken cancellationToken)
            {
                _visual?.Stop();
                if (_visual is not null)
                {
                    await _visual.DrainAsync();
                }

                if (_replacement is not null)
                {
                    await _replacement.DrainCandidateAsync(cancellationToken);
                }
            }

            public ValueTask RunContentCleanupAsync(Func<CancellationToken, ValueTask> cleanup, CancellationToken cancellationToken)
            {
                slot._application.Owner.CheckOwner();
                ArgumentNullException.ThrowIfNull(cleanup);
                ObjectDisposedException.ThrowIf(_phase == ChangePhase.Disposed, this);
                if (_cleanup is { IsCompleted: false })
                {
                    throw new InvalidOperationException("A content cleanup call is already running.");
                }

                _cleanup = slot._application.Owner.TrackAsync(() => cleanup(CancellationToken.None).AsTask());
                return new(_cleanup.WaitAsync(cancellationToken));
            }

            public async ValueTask DiscardAsync(CancellationToken cancellationToken)
            {
                if (_phase is ChangePhase.Committed or ChangePhase.Released)
                {
                    throw new InvalidOperationException("A published view cannot be discarded.");
                }

                _visual?.Stop();
                if (_visual is not null)
                {
                    await _visual.DrainAsync();
                }

                _owner?.Discard();
                if (_candidate is { } candidate)
                {
                    candidate.BeginStop(false);
                    while (!candidate.IsStopped)
                    {
                        await slot._application.NextTurnAsync().AsTask().WaitAsync(cancellationToken);
                    }
                }

                if (_replacement is not null)
                {
                    await _replacement.DiscardAsync(cancellationToken);
                }

                if (_candidate is not null)
                {
                    await _candidate.ReleaseTransferredAsync();
                    _candidate = null;
                }

                foreach (IUiApplicationViewDiagnosticChange change in _diagnostics)
                {
                    await change.DiscardAsync(cancellationToken);
                }

                if (!slot._stopping && _previous is { } previous)
                {
                    if (_bookmark is not null)
                    {
                        previous.Surface.TryRestoreFocus(_bookmark);
                    }

                    if (_gated)
                    {
                        slot._application.Popups.GateOwner(previous.Owner!, false);
                    }

                    previous.Suspended = false;
                    previous.Surface.UpdateLayout();
                    previous.Surface.SetSignalUpdatesEnabled(true);
                    previous.Surface.SetAnimationUpdatesEnabled(true);
                    previous.Portals?.SetUpdatesEnabled(true);
                    previous.Host.Resume();
                }

                _phase = ChangePhase.Discarded;
                host._preparing = null;
            }

            public async ValueTask DisposeAsync()
            {
                if (_phase == ChangePhase.Disposed)
                {
                    return;
                }

                if (_cleanup is { IsCompleted: false })
                {
                    throw new InvalidOperationException("Await content cleanup before returning the token.");
                }

                if (_phase is not (ChangePhase.Discarded or ChangePhase.Released))
                {
                    throw new InvalidOperationException("Complete view cleanup before returning the token.");
                }

                if (_replacement is not null)
                {
                    await _replacement.DisposeAsync();
                }

                foreach (IUiApplicationViewDiagnosticChange change in _diagnostics)
                {
                    await change.DisposeAsync();
                }

                _phase = ChangePhase.Disposed;
            }
        }
    }
}
