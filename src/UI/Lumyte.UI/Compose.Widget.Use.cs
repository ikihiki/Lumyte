namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive configuration; mounted state and layout belong to an element.</summary>
        public abstract partial class Widget
        {
            internal UiUseEnvironment? ProvidedContexts { get; set; }
        }
    }
}
