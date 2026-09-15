using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.Native.Passes;

public static class NativeImageProcessingPasses
{
    public static NativeRenderPassRegistry AddImageProcessing(this NativeRenderPassRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(ClearPassContract.Instance, static _ => new NativeClearPass());
        registry.Register(TextureCopyPassContract.Instance, static _ => new NativeTextureCopyPass());
        registry.Register(OutputPassContract.Instance, static services => new NativeOutputPass(services));
        registry.Register(BlitPassContract.Instance, static services => new NativeFilterPass(services));
        registry.Register(BlurPassContract.Instance, static services => new NativeFilterPass(services));
        registry.Register(CompositePassContract.Instance, static services => new NativeFilterPass(services));
        registry.Register(ToneMapPassContract.Instance, static services => new NativeFilterPass(services));
        return registry;
    }
}
