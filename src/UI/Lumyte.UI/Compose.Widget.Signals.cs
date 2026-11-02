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

            internal Func<Widget>? Construction { get; set; }

            internal SignalReadSet? ConstructionReads { get; set; }

            internal UiDeclarationRecipe? DeclarationRecipe { get; set; }

            internal IReadOnlyDictionary<object, UiSignalArgument>? SignalArguments => _signalArguments;

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
