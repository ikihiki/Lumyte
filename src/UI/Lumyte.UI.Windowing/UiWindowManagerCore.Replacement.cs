namespace Lumyte.UI.Windowing;

internal sealed partial class UiWindowManagerCore
{
    public IUiWindowPresentationReplacement BeginReplacement(UiWindowId window, UiWindowBinding? current, UiSurface candidate)
    {
        CheckAdmission();
        ArgumentNullException.ThrowIfNull(candidate);
        Node node = Get(window);
        if (node.Terminal || node.Retiring || node.Replacement is not null || !ReferenceEquals(current, node.Binding) || (current is null && node.Surface is not null) || _nodes.Values.Any(n => ReferenceEquals(n.Surface, candidate) || ReferenceEquals(n.Replacement?.Candidate, candidate)))
        {
            throw new InvalidOperationException("The binding is not eligible for replacement.");
        }

        var replacement = new Replacement(this, node, candidate);
        node.Replacement = replacement;
        return replacement;
    }

    private sealed class Replacement(UiWindowManagerCore manager, Node node, UiSurface candidate) : IUiWindowPresentationReplacement
    {
        private readonly UiWindowBinding _binding = new(node.Id, () => manager.Retire(node));
        private readonly TaskCompletionSource _retired = new();
        private readonly CancellationTokenSource _cancellation = new();
        private IUiWindowPresentation? _previous = node.Token;
        private UiWindowBinding? _previousBinding = node.Binding;
        private Func<ValueTask>? _releasePrevious = node.Token is { } token ? token.DisposeAsync : null;
        private IUiStagedWindowPresentation? _stage;
        private UiWindowPresentationView? _view;
        private UiWindowSourceSnapshot? _state;
        private object? _root;
        private object? _inputVersion;
        private bool _ready;
        private bool _committed;
        private bool _discarded;
        private bool _released;
        private bool _disposed;

        internal UiSurface Candidate => candidate;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            if (_stage is not null || _ready || _disposed)
            {
                throw new InvalidOperationException("Prepare a replacement once.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            manager.Refresh(node);
            if (node.Source is null)
            {
                manager.Initialize(node);
            }

            if (node.Source is not IUiWindowPresentationStager stager)
            {
                throw new NotSupportedException("presentation-replacement-unavailable");
            }

            if (node.Desired is null)
            {
                node.Surface = candidate;
                try
                {
                    manager.UpdateGeometry(node);
                }
                finally
                {
                    node.Surface = null;
                }
            }

            _state = node.Source.CaptureState();
            _view = node.Desired!;
            candidate.SetViewport(node.Size);
            candidate.UpdateLayout();
            _stage = stager.BeginReplacement(_previous, _view, candidate);
            await _stage.PrepareAsync(cancellationToken);
            _root = candidate.Root;
            _inputVersion = candidate.InputVersionForWindowing;
            _ready = true;
        }

        public bool TryValidateCommit(out string code)
        {
            code = "candidate-stale";
            if (!_ready || _committed || _discarded || manager._stopping || node.Terminal || node.Retiring || node.Source!.CaptureState() != _state || node.Desired != _view || !ReferenceEquals(node.Binding, _previousBinding) || !ReferenceEquals(candidate.Root, _root) || !ReferenceEquals(candidate.InputVersionForWindowing, _inputVersion))
            {
                return false;
            }

            return _stage!.TryValidateCommit(out code);
        }

        public UiWindowBinding Commit()
        {
            IUiWindowPresentation token = _stage!.Commit();
            _previousBinding?.Redirect(_releasePrevious!);
            node.Token = token;
            node.Surface = candidate;
            node.Binding = _binding;
            node.Committed = null;
            node.Retired = _retired;
            node.Cancellation?.Dispose();
            node.Cancellation = _cancellation;
            node.Phase = node.Registration is not null ? UiWindowState.Open : node.ShowIssued ? UiWindowState.Open : UiWindowState.Ready;
            node.Replacement = null;
            _committed = true;
            return _binding;
        }

        public async ValueTask DrainPreviousAsync(CancellationToken cancellationToken)
        {
            if (_previous is not null)
            {
                await _previous.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }
        }

        public async ValueTask ReleasePreviousAsync(CancellationToken cancellationToken)
        {
            if (_previousBinding is not null)
            {
                await _previousBinding.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }

            _previous = null;
            _previousBinding = null;
            _releasePrevious = null;
            _released = true;
        }

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => _stage?.DrainCandidateAsync(cancellationToken) ?? ValueTask.CompletedTask;

        public async ValueTask DiscardAsync(CancellationToken cancellationToken)
        {
            if (_committed)
            {
                throw new InvalidOperationException("A committed replacement cannot be discarded.");
            }

            if (_stage is not null)
            {
                await _stage.DiscardAsync(cancellationToken);
            }

            node.Replacement = null;
            _discarded = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (!_discarded && (!_committed || !_released))
            {
                throw new InvalidOperationException("Discard or release the previous binding before returning the token.");
            }

            if (_stage is not null)
            {
                await _stage.DisposeAsync();
            }

            if (!_committed)
            {
                _cancellation.Dispose();
            }

            _disposed = true;
        }
    }
}
