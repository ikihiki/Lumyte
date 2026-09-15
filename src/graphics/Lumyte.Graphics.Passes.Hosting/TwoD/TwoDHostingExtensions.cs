using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.Passes;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.Passes;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Passes.Hosting;

public static class TwoDHostingExtensions
{
    /// <summary>Registers both implementations of the shared 2D feature contract.</summary>
    public static LumyteGraphicsBuilder Add2DRendering(this LumyteGraphicsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        { return builder; }
        builder.Services.AddSingleton(new RegistrationMarker());
        builder.AddNativePasses(static (_, registry) => registry.Add2DRendering());
        builder.AddPortablePasses(static (_, registry) => registry.Add2DRendering());
        builder.Configure(options => options.Runtime = options.Runtime with
        {
            RequiredPasses = options.Runtime.RequiredPasses.Append(new GpuRenderPassId(
                Draw2DPassContract.Instance.Id, Draw2DPassContract.Instance.Version)).Distinct().ToArray(),
        });
        return builder;
    }

    private sealed class RegistrationMarker;
}
