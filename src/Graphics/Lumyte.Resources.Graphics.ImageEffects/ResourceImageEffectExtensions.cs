using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Loads the configured shader package and appends effects with graph-owned lifetimes.</summary>
public static class ResourceImageEffectExtensions
{
    private static readonly ResourceKey<ImageEffectShaderResource> _shaderKey = new("Lumyte/ImageEffects");

    /// <summary>Loads the default or injected shader package once and follows its published generations.</summary>
    /// <param name="manager">The manager configured with AddImageEffects.</param>
    /// <param name="cancellationToken">Cancels this acquisition.</param>
    /// <returns>A reusable reference; each appended effect pins its own exact generation.</returns>
    public static ValueTask<ResourceReference<ImageEffectShaderResource>> AcquireImageEffectShadersAsync(this ResourceManager manager, CancellationToken cancellationToken = default)
        => manager.AcquireAsync(_shaderKey, cancellationToken);

    /// <summary>Appends an effect graph, transferring its fixed resources and shader lease to the imports.</summary>
    /// <param name="imports">The open scope whose Compile method owns all appended effects.</param>
    /// <param name="shaders">The preloaded default or injected package.</param>
    /// <param name="effects">The immutable effect graph.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <param name="snapshot">An optional shared publication view for the whole frame.</param>
    /// <returns>The graph-local result; the scope owns preparation and cleanup.</returns>
    /// <remarks>Discard the graph if preparation fails. Submit and GPU completion remain explicit.</remarks>
    public static EffectImage AppendEffects(this ResourceRenderGraphImports imports, ResourceReference<ImageEffectShaderResource> shaders, ImageEffectGraph effects, EffectImage source, ResourceSnapshot? snapshot = null)
    {
        EffectLifetime lifetime = Retain(imports, shaders, source.ColorSemantic, snapshot);
        return imports.Graph.AppendEffectsWithContext(effects, source, lifetime.Context);
    }

    /// <summary>Appends one effect with the same automatic resource ownership as an effect graph.</summary>
    /// <param name="imports">The open import scope.</param>
    /// <param name="shaders">The preloaded default or injected package.</param>
    /// <param name="effect">The immutable effect definition.</param>
    /// <param name="inputs">The graph-local inputs.</param>
    /// <returns>The graph-local result, owned through the compiled plan.</returns>
    public static EffectImage AppendEffect(this ResourceRenderGraphImports imports, ResourceReference<ImageEffectShaderResource> shaders, ImageEffectDefinition effect, params EffectImage[] inputs)
    {
        EffectColorSemantic semantic = inputs.Length == 0 ? EffectColorSemantic.DisplayLinear : inputs[0].ColorSemantic;
        EffectLifetime lifetime = Retain(imports, shaders, semantic, null);
        return imports.Graph.AppendEffectWithContext(effect, lifetime.Context, inputs);
    }

    private static EffectLifetime Retain(ResourceRenderGraphImports imports, ResourceReference<ImageEffectShaderResource> shaders, EffectColorSemantic semantic, ResourceSnapshot? snapshot)
    {
        ResourceLease<ImageEffectShaderResource> lease = shaders.Acquire(snapshot);
        try
        {
            ImageEffectShaderResource resource = lease.Value;
            if (!ReferenceEquals(imports.Graph.Device, resource.Device))
            {
                throw new InvalidOperationException("The image effects and graph must use the same physical device.");
            }

            var lifetime = new EffectLifetime(lease, new(resource.Device, semantic, resource.Shaders));
            imports.Retain(lifetime);
            return lifetime;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private sealed class EffectLifetime(ResourceLease<ImageEffectShaderResource> lease, EffectPreparationContext context) : IRenderGraphLifetime
    {
        internal EffectPreparationContext Context { get; } = context;

        public void Validate()
        {
            _ = lease.Value;
            ((IRenderGraphLifetime)Context).Validate();
        }

        public void Dispose()
        {
            Context.Dispose();
            lease.Dispose();
        }
    }
}
