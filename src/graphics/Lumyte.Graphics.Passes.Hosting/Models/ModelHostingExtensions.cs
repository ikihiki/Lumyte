using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.Passes;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.Passes;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Passes.Hosting;

public static class ModelHostingExtensions
{
    public static LumyteGraphicsBuilder AddModelRendering(this LumyteGraphicsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(d => d.ServiceType == typeof(RegistrationMarker))) { return builder; }
        builder.Services.AddSingleton(new RegistrationMarker());
        builder.AddNativePasses(static (_, registry) => registry.AddModelRendering());
        builder.AddPortablePasses(static (_, registry) => registry.AddModelRendering());
        builder.Configure(options => options.Runtime = options.Runtime with
        { RequiredPasses = options.Runtime.RequiredPasses.Append(new GpuRenderPassId(ModelPassContract.Instance.Id,ModelPassContract.Instance.Version)).Distinct().ToArray() });
        return builder;
    }
    private sealed class RegistrationMarker;
}
