using Lumyte.Composition;
using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        public abstract partial class Widget : ICompositionRebuildable
        {
            private Dictionary<object, Func<bool>>? _signalArguments;
            private Dictionary<object, IUiAnimationProjection>? _animationArguments;
            private List<Action>? _animationInitializers;
            private Func<object>? _compositionFactory;

            internal Func<Widget>? Construction { get; set; }

            internal UiDeclarationRecipe? DeclarationRecipe { get; set; }

            internal ulong DeclarationRecipeRevision { get; set; }

            internal SignalReadSet? ConstructionReads { get; set; }

            internal IEnumerable<Func<bool>> SignalArguments => _signalArguments is { } arguments ? arguments.Values : [];

            internal IReadOnlyDictionary<object, IUiAnimationProjection>? AnimationArguments => _animationArguments;

            internal Func<object>? CompositionFactory => _compositionFactory;

            /// <inheritdoc/>
            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory)
            {
                ArgumentNullException.ThrowIfNull(factory);
                if (Surface is not null || _compositionFactory is not null)
                {
                    throw new InvalidOperationException("A declaration recipe can be assigned once, before attachment.");
                }

                _compositionFactory = factory;
            }

            internal void AddAnimationArgument(object key, IUiAnimationProjection projection, Action initialize)
            {
                (_animationArguments ??= []).Add(key, projection);
                (_animationInitializers ??= []).Add(initialize);
            }

            internal void InitializeAnimationArguments()
            {
                if (Surface is null && _animationInitializers is { } initializers)
                {
                    foreach (Action initialize in initializers)
                    {
                        initialize();
                    }
                }
            }

            internal void AddSignalArgument(object key, Func<bool> reader)
            {
                if (Surface is not null || !(_signalArguments ??= []).TryAdd(key, reader))
                {
                    throw new InvalidOperationException("Signal arguments must be unique and attached before connection.");
                }
            }

            internal bool TryReuseSignalLayout(Widget previous)
            {
                if (Width != previous.Width || Height != previous.Height || LayoutVisible != previous.LayoutVisible || !CanReuseLayout(previous))
                {
                    return false;
                }

                _size = previous._size;
                _bounds = previous._bounds;
                _isLayoutDirty = previous._isLayoutDirty;
                return true;
            }

            internal bool TryReuseSignalInput(Widget previous, Dictionary<Widget, Widget> matches)
            {
                if (IsEnabled != previous.IsEnabled || HitTestVisible != previous.HitTestVisible || Focusable != previous.Focusable
                    || ClipToBounds != previous.ClipToBounds || LayoutVisible != previous.LayoutVisible || IsTransitioning != previous.IsTransitioning || !CanReuseInput(previous))
                {
                    return false;
                }

                var children = new Widget[previous._inputChildren.Count];
                for (int index = 0; index < children.Length; index++)
                {
                    if (!matches.TryGetValue(previous._inputChildren[index], out Widget? child))
                    {
                        return false;
                    }

                    children[index] = child;
                }

                InputVersion = previous.InputVersion;
                InputBounds = previous.InputBounds;
                InputOrigin = previous.InputOrigin;
                InputFocusable = previous.InputFocusable;
                _inputSubtreeBounds = previous._inputSubtreeBounds;
                _inputChildren = children;
                return true;
            }

            /// <summary>Opts a component into runtime input geometry reuse for equivalent declarations.</summary>
            /// <param name="previous">The matched previous declaration.</param>
            /// <returns>Whether component-specific input eligibility is unchanged.</returns>
            protected virtual bool CanReuseInput(Widget previous) => false;

            /// <summary>Opts a component into geometry reuse when its own layout settings are equivalent.</summary>
            /// <param name="previous">The matched previous declaration.</param>
            /// <returns>Whether component-specific layout settings are equivalent.</returns>
            protected virtual bool CanReuseLayout(Widget previous) => false;
        }
    }
}
