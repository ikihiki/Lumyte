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
        => manager.AcquireAsync(_shaderKey, cancellationToken);

    /// <summary>Loads the configured shaders once and registers their graph-local image-effects context.</summary>
    /// <param name="extensions">The configuration with AddResources registered for the same manager.</param>
    /// <param name="manager">The manager owning the default or injected shader package.</param>
    /// <param name="cancellationToken">Cancels preload without leaving a registered owner.</param>
    /// <returns>The supplied configuration, owning the reference until configuration disposal.</returns>
    /// <remarks>Finish registration before creating graphs. No resource loading occurs in AppendEffects.</remarks>
    public static async ValueTask<RenderGraphExtensions> AddImageEffectsAsync(this RenderGraphExtensions extensions, ResourceManager manager, CancellationToken cancellationToken = default)
    {
        ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return extensions.Add<ImageEffectsGraphContext>(graph => CreateContext(graph, reference), reference);
        }
        catch
        {
            reference.Dispose();
            throw;
        }
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
