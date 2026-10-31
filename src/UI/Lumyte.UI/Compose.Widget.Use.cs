namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Base declaration and live layout storage for a UI node.</summary>
        public abstract partial class Widget
        {
            internal UiUseEnvironment? ProvidedContexts { get; set; }

            internal UiUseEnvironment? BuildContexts { get; set; }
        }
    }
}
