namespace Lumyte.Resources;

/// <summary>Reports a completed automatic reload attempt, including unchanged and conflicting results.</summary>
/// <param name="result">The attempt's publication result.</param>
public sealed class ResourceReloadEventArgs(ResourceReloadResult result) : EventArgs
{
    /// <summary>Gets the completed attempt's result.</summary>
    public ResourceReloadResult Result { get; } = result;
}
