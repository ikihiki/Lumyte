using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>Provides static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains passive component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration.</summary>
        public abstract partial class Widget
        {
            private Dictionary<object, UiSignalArgument>? _signalArguments;
            private Dictionary<object, IUiAnimationProjection>? _animationArguments;
            private List<Func<bool>>? _animationInitializers;

            internal Func<Widget>? Construction { get; set; }

            internal SignalReadSet? ConstructionReads { get; set; }

            internal UiDeclarationRecipe? DeclarationRecipe { get; set; }

            internal IReadOnlyDictionary<object, UiSignalArgument>? SignalArguments => _signalArguments;

            internal IReadOnlyDictionary<object, IUiAnimationProjection>? AnimationArguments => _animationArguments;

            internal bool NeedsAnimationRefresh => _animationInitializers?.Any(read => read()) == true;

            internal void AddAnimationArgument(object key, IUiAnimationProjection projection, Func<bool> initialize)
            {
                (_animationArguments ??= []).Add(key, projection);
                (_animationInitializers ??= []).Add(initialize);
            }

            internal void AddSignalArgument(object key, object source, Func<bool> reader)
            {
                if (!(_signalArguments ??= []).TryAdd(key, new(source, reader)))
                {
                    throw new InvalidOperationException("Signal argument member keys must be unique.");
                }
            }
        }
    }
}
