using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class SignalBinding(Func<Widget> build) : IDisposable
    {
        private readonly List<SignalRegion> _regions = [];

        internal Func<Widget> Build { get; } = build;

        internal bool AllowsLocalRebuild { get; private set; } = true;

        internal bool HasChanges => _regions.Any(region => region.HasChanges);

        internal bool HasRootChanges => _regions.Any(region => region.Boundary is null && region.HasChanges);

        internal ulong Revision => _regions.Count == 0 ? 0 : _regions.Max(region => region.Revision);

        public void Dispose()
        {
            foreach (SignalRegion region in _regions)
            {
                region.Dispose();
            }

            _regions.Clear();
        }

        internal static SignalBinding? Create(UiSurface surface, Widget root, Widget? previous)
        {
            List<Widget> nodes = root.CollectSubtree();
            if (!nodes.Any(node => node.SignalArguments.Any()) && root.ConstructionReads is not { IsEmpty: false })
            {
                return null;
            }

            var recipe = UiDeclarationRecipe.Capture(root);
            Func<Widget> build = (root.ConstructionReads is { IsEmpty: false } ? root.Construction : recipe is not null ? recipe.Build : root.Construction)
                ?? throw new InvalidOperationException("Signal declarations need generated composition factories or a surface construction function.");
            var binding = new SignalBinding(build);
            var outer = new SignalRegion(surface, null, root.ConstructionReads);
            binding._regions.Add(outer);
            var pending = new System.Collections.Generic.Stack<(Widget Node, SignalRegion Region)>();
            pending.Push((root, outer));
            try
            {
                surface.InheritAnimationArguments(root, previous);
                while (pending.TryPop(out (Widget Node, SignalRegion Region) item))
                {
                    (Widget node, SignalRegion region) = item;
                    binding.AllowsLocalRebuild &= node.CanRebuildLocally;
                    if (node is UiFunctionWidget function && function.CompositionFactory is not null)
                    {
                        region = new(surface, function, null);
                        binding._regions.Add(region);
                    }

                    region.Nodes.Add(node);
                    foreach (WidgetChildren slot in node.ChildCollections)
                    {
                        if (!slot.IsPortal)
                        {
                            foreach (Widget child in slot)
                            {
                                pending.Push((child, region));
                            }
                        }
                    }
                }

                for (int index = binding._regions.Count - 1; index >= 0; index--)
                {
                    SignalRegion region = binding._regions[index];
                    if (!region.Initialize())
                    {
                        region.Dispose();
                        binding._regions.RemoveAt(index);
                    }
                }

                return binding;
            }
            catch
            {
                binding.Dispose();
                throw;
            }
        }

        internal UiFunctionWidget[] ChangedFunctions()
        {
            var dirty = _regions.Where(region => region.Boundary is not null && region.HasChanges).Select(region => region.Boundary!).ToHashSet();
            return dirty.Where(function => !HasDirtyAncestor(function)).ToArray();

            bool HasDirtyAncestor(Widget function)
            {
                for (Widget? ancestor = function.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ancestor is UiFunctionWidget parent && dirty.Contains(parent))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void Replace(IReadOnlySet<Widget> previous, SignalBinding?[] candidates)
        {
            for (int index = _regions.Count - 1; index >= 0; index--)
            {
                SignalRegion region = _regions[index];
                if (region.Boundary is { } boundary && previous.Contains(boundary))
                {
                    region.Dispose();
                    _regions.RemoveAt(index);
                }
            }

            foreach (SignalBinding? candidate in candidates)
            {
                if (candidate is not null)
                {
                    _regions.AddRange(candidate._regions);
                    candidate._regions.Clear();
                }
            }
        }

        internal void Pause()
        {
            foreach (SignalRegion region in _regions)
            {
                region.Pause();
            }
        }

        internal void Resume()
        {
            // A resumed observer can synchronously replace this binding.
            foreach (SignalRegion region in _regions.ToArray())
            {
                region.Resume();
            }
        }
    }

    private sealed class SignalRegion(UiSurface surface, UiFunctionWidget? boundary, SignalReadSet? reads) : ISignalScheduler, IDisposable
    {
        private readonly SignalScope _scope = new();
        private SignalEffect? _effect;
        private AnimationLease? _animations;
        private bool _dirty;
        private bool _disposed;

        internal UiFunctionWidget? Boundary { get; } = boundary;

        internal List<Widget> Nodes { get; } = [];

        internal bool HasChanges => _dirty || _effect?.IsPending == true;

        internal ulong Revision => _effect?.Revision ?? 0;

        public void Schedule(Action work) => surface.PostSignal(work);

        public void Dispose()
        {
            _disposed = true;
            _scope.Dispose();
            _animations?.Dispose();
        }

        internal bool Initialize()
        {
            Func<bool>[] readers = Nodes.SelectMany(node => node.SignalArguments).ToArray();
            if (readers.Length == 0 && reads is not { IsEmpty: false })
            {
                return false;
            }

            _animations = new(surface, Nodes.SelectMany(node => node.AnimationArguments?.Values ?? []).Distinct().ToArray());
            foreach (Widget node in Nodes)
            {
                node.InitializeAnimationArguments();
            }

            _scope.Run(() => _effect = Signal.Effect(
                () =>
                {
                    foreach (Func<bool> reader in readers)
                    {
                        _dirty |= reader();
                    }

                    if (reads is not null)
                    {
                        reads.Read();
                        _dirty |= !reads.IsCurrent;
                    }
                },
                this,
                error => surface._signalFailure = new UiStateUpdateException(error)));
            Nodes.Clear();
            return true;
        }

        internal void Pause()
        {
            if (!_disposed && _effect is { Failure: null } effect)
            {
                effect.Pause();
            }
        }

        internal void Resume()
        {
            if (!_disposed)
            {
                _effect?.Resume();
            }
        }
    }
}
