namespace Lumyte.UI;

/// <summary>Describes the proposed construction environment without publishing instance changes.</summary>
public sealed class UiElementBuildContext
{
    internal UiElementBuildContext(UiElement? parent, UiUseEnvironment? environment, List<Action> cleanup)
    {
        Parent = parent;
        Environment = environment;
        Cleanup = cleanup;
    }

    /// <summary>Gets the proposed parent instance, whose configuration may still await publication.</summary>
    public UiElement? Parent { get; }

    internal UiUseEnvironment? Environment { get; }

    private List<Action> Cleanup { get; }

    internal void AfterPreparation(Action cleanup) => Cleanup.Add(cleanup);
}
