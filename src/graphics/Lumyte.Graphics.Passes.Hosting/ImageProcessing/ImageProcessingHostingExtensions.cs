using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.Passes;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.Passes;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Passes.Hosting;

public static class ImageProcessingHostingExtensions
{
    public static LumyteGraphicsBuilder AddImageProcessing(this LumyteGraphicsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            return builder;
        }
        builder.Services.AddSingleton(new RegistrationMarker());
        builder.AddNativePasses(static (_, registry) => registry.AddImageProcessing());
        builder.AddPortablePasses(static (_, registry) => registry.AddImageProcessing());
        builder.Configure(options => options.Runtime = options.Runtime with
        {
            RequiredPasses = options.Runtime.RequiredPasses.Concat(new GpuRenderPassId[]
            {
                new(ClearPassContract.Instance.Id, ClearPassContract.Instance.Version),
                new(TextureCopyPassContract.Instance.Id, TextureCopyPassContract.Instance.Version),
                new(OutputPassContract.Instance.Id, OutputPassContract.Instance.Version),
                new(BlitPassContract.Instance.Id, BlitPassContract.Instance.Version),
                new(BlurPassContract.Instance.Id, BlurPassContract.Instance.Version),
                new(CompositePassContract.Instance.Id, CompositePassContract.Instance.Version),
                new(ToneMapPassContract.Instance.Id, ToneMapPassContract.Instance.Version),
            }).Distinct().ToArray(),
        });
        return builder;
    }

    private sealed class RegistrationMarker;
}
