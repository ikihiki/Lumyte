using Lumyte.Graphics.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Owns the originating generic execution and source leases until explicit retirement.</summary>
public sealed class PreparedPipelineExecution2D : IDisposable
{
    private readonly PipelineOwnerGroup2D _owners;

    internal PreparedPipelineExecution2D(GraphicsPipelineExecution execution, PipelineOwnerGroup2D owners)
    {
        Execution = execution;
        _owners = owners;
    }

    /// <summary>Gets the exact generic execution accepted only by the matching explicit host.</summary>
    public GraphicsPipelineExecution Execution { get; }

    /// <summary>Returns owners only after execution retirement; failed cleanup remains retryable.</summary>
    public void Dispose() => _owners.Dispose();
}
