using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A typed preparation and recording contract.</summary>
internal interface IRasterOperation
{
    /// <summary>Resolves native bindings during preparation.</summary>
    /// <param name="resources">The prepared resources.</param>
    void Prepare(PreparedResources resources);

    /// <summary>Selects the typed pipeline.</summary>
    /// <param name="encoder">The native encoder.</param>
    void SelectPipeline(IRenderEncoder encoder);

    /// <summary>Records the prepared operation.</summary>
    /// <param name="encoder">The native encoder.</param>
    void Record(IRenderEncoder encoder);

    /// <summary>Prepares native roots, descriptors and pipeline variants without emitting GPU instructions.</summary>
    /// <typeparam name="TArguments">The immutable argument ABI.</typeparam>
    /// <param name="commands">The sole native command owner.</param>
    /// <param name="pass">The captured render target scope.</param>
    /// <param name="state">The immutable raster state.</param>
    /// <param name="table">The coherent descriptor generation.</param>
    /// <param name="arguments">The stable identified prepared arguments.</param>
    void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments;

    /// <summary>Selects prepared index bindings before native prepared-argument validation.</summary>
    /// <param name="encoder">The native encoder.</param>
    void BindInputs(IRenderEncoder encoder)
    {
    }
}
