namespace Lumyte.Pipelines;

/// <summary>Provides only the capabilities bound by this build.</summary>
public sealed class StageBuildContext
{
    internal StageBuildContext(IStageCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    /// <summary>Gets the explicitly supplied capabilities.</summary>
    public IStageCapabilities Capabilities { get; }
}
