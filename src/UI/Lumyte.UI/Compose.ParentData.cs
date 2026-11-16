namespace Lumyte.UI;

/// <summary>Provides passive UI factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Stores immutable placement metadata.</summary>
        public abstract partial class Widget
        {
            internal UiParentData[] ParentData { get; private set; } = [];

            internal void AddParentData(UiParentData data) => ParentData = [.. ParentData, data];
        }
    }
}
