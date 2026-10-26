using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a module from a tracked artifact or a device-independent embedded artifact loader.</summary>
public sealed class GraphicsShaderDefinition : GraphicsResourceDefinition<IGraphicsShader>
{
    private readonly ResourceKey<ShaderArtifact>? _artifact;
    private readonly Lazy<ShaderArtifact>? _embedded;

    /// <summary>Initializes a new instance of the <see cref="GraphicsShaderDefinition"/> class.</summary>
    /// <param name="artifact">The tracked CPU artifact key.</param>
    public GraphicsShaderDefinition(ResourceKey<ShaderArtifact> artifact)
    {
        _artifact = artifact;
    }

    /// <summary>Initializes a new instance of the <see cref="GraphicsShaderDefinition"/> class.</summary>
    /// <param name="loadArtifact">Loads immutable CPU data without capturing a device; evaluated once.</param>
    public GraphicsShaderDefinition(Func<ShaderArtifact> loadArtifact)
    {
        ArgumentNullException.ThrowIfNull(loadArtifact);
        _embedded = new(loadArtifact);
    }

    /// <inheritdoc/>
    public override async ValueTask<IGraphicsShader> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ShaderArtifact artifact = _artifact is { } key ? await context.RequireAsync(key, cancellationToken: cancellationToken) : _embedded!.Value;
        cancellationToken.ThrowIfCancellationRequested();
        return device.CreateShader(artifact);
    }
}
