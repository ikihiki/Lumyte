namespace Lumyte.UI;

/// <summary>Describes the proposed construction environment without publishing instance changes.</summary>
public sealed class UiElementBuildContext
{
    internal UiElementBuildContext(UiElement? parent, UiUseEnvironment? environment)
    {
        Parent = parent;
        Environment = environment;
    }

    /// <summary>Gets the proposed parent instance, whose configuration may still await publication.</summary>
    public UiElement? Parent { get; }

    internal UiUseEnvironment? Environment { get; }
}
