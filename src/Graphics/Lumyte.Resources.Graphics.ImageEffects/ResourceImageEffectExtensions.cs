using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Preloads managed shaders and supplies them to the ordinary graph image-effects extension.</summary>
public static class ResourceImageEffectExtensions
{
    private static readonly ResourceKey<ImageEffectShaderResource> _shaderKey = new("Lumyte/ImageEffects");

    /// <summary>Loads the default or injected shader package and follows its published generations.</summary>
    /// <param name="manager">The manager configured with the image-effects resource pipeline.</param>
    /// <param name="cancellationToken">Cancels this acquisition.</param>
    /// <returns>A reusable reference to the shader package.</returns>
    public static ValueTask<ResourceReference<ImageEffectShaderResource>> AcquireImageEffectShadersAsync(this ResourceManager manager, CancellationToken cancellationToken = default)
        => manager.RetainAsync(_shaderKey, cancellationToken);

    /// <summary>Registers managed image effects, deferring shader loading until BuildAsync.</summary>
    /// <param name="extensions">The builder with AddResources registered for the same manager.</param>
    /// <param name="manager">The manager owning the default or injected shader package.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>BuildAsync preloads the shaders and transfers the reference to the built configuration. AppendEffects performs no resource loading.</remarks>
    public static RenderGraphExtensionsBuilder AddImageEffects(this RenderGraphExtensionsBuilder extensions, ResourceManager manager)
        => extensions.AddAsync<ImageEffectsGraphContext>(cancellationToken => PrepareShadersAsync(manager, cancellationToken));

    private static async ValueTask<RenderGraphExtensionFactory<ImageEffectsGraphContext>> PrepareShadersAsync(ResourceManager manager, CancellationToken cancellationToken)
    {
        ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync(cancellationToken).ConfigureAwait(false);
        return new(graph => CreateContext(graph, reference), reference);
    }

    private static ImageEffectsGraphContext CreateContext(RenderGraphBuilder graph, ResourceReference<ImageEffectShaderResource> reference)
    {
        ImageEffectShaderResource resource = graph.ImportResources().Acquire(reference);
        if (!ReferenceEquals(graph.Device, resource.Device))
        {
            throw new InvalidOperationException("The image effects and graph must use the same physical device.");
        }

        return new(graph, resource.Shaders);
    }
}
