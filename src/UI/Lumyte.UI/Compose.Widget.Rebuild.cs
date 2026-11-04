using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration with construction-time child ownership.</summary>
        public abstract partial class Widget : ICompositionRebuildable
        {
            private Func<object>? _compositionFactory;

            internal Func<object>? CompositionFactory => _compositionFactory;

            internal bool RebuildsChildTree => RebuildsChildren;

            /// <summary>Gets a value indicating whether rebuilding this declaration constructs its child tree anew.</summary>
            protected virtual bool RebuildsChildren => false;

            /// <inheritdoc/>
            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory)
            {
                ArgumentNullException.ThrowIfNull(factory);
                if (_compositionFactory is not null)
                {
                    throw new InvalidOperationException("A declaration recipe can be assigned once during construction.");
                }

                _compositionFactory = factory;
            }
        }
    }
}
