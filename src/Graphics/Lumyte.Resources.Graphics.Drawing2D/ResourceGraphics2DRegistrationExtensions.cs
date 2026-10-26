using Lumyte.Resources.DependencyInjection;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Registers renderer resources independently of drawing strategy composition.</summary>
public static class ResourceGraphics2DRegistrationExtensions
{
    /// <summary>Adds default 2D shader and renderer resource stages.</summary>
    /// <param name="builder">The resource pipeline configuration.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Use UseGraphicsShaders for Renderer2DResource to select other shader artifacts.</remarks>
    public static ResourceRegistrationBuilder AddGraphics2D(this ResourceRegistrationBuilder builder)
        => builder.AddGraphicsResources()
            .ConfigureServices(DrawingGraphicsResources2D.RegisterDefaults)
            .TryAddSourcePipeline<Renderer2DResource, Renderer2DResourceStage, GraphicsResourceRuntime>();
}
