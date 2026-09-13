using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;
using Lumyte.Graphics.Portable.Shaders;
using Lumyte.Graphics.RenderGraph;
using Inputs = Consumer.@namespace.@struct;

namespace Lumyte.Graphics.Portable.RenderGraph.Generators.Tests;

public sealed class GeneratedPassBindingInputTests
{
    [Theory]
    [InlineData(8ul, 16ul)]
    [InlineData(8ul, ulong.MaxValue)]
    public async Task PreparedBindingsResolveLogicalResourcesAndSelectedBufferRange(ulong offset, ulong length)
    {
        var backend = new ManagerTestBackend();
        TestPass? pass = null;
        var passes = new PortableRenderPassRegistry();
        passes.Register(TestContract.Instance, services => pass = new(services, offset, length));
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), passes);
        await using IGpuRenderRuntime runtime = await provider.CreateAsync(new());
        var graph = new GpuRenderGraph();
        GpuRenderGraphBuffer target = graph.CreateBuffer("target", new(length == ulong.MaxValue ? 64 - offset : length));
        graph.AddPass("bindings", TestContract.Instance, target);
        graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(pass!.Expected, Assert.Single(backend.CreatedBindings).Entries);
    }

    private sealed class TestContract : IGpuRenderPassContract<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        internal static TestContract Instance { get; } = new();
        public string Id => "test.generated-pass-bindings";
        public int Version => 1;
        public GpuRenderGraphBuffer Snapshot(GpuRenderGraphBuffer request) => request;
        public GpuRenderGraphBuffer Declare(GpuPassDeclarationContext context, GpuRenderGraphBuffer request)
        { context.Write(request); return request; }
    }

    private sealed class TestPass(PortablePassServices services, ulong offset, ulong length)
        : IPortableRenderPass<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        private GpuResourceScope? scope;
        private PortableShaderProgram? program;
        internal GpuBindingEntry[] Expected { get; private set; } = [];

        public ValueTask BuildAsync(PortablePassBuildContext context, GpuRenderGraphBuffer request,
            GpuRenderGraphBuffer result, CancellationToken cancellationToken)
        {
            scope = services.Resources.CreateScope();
            program = services.ShaderLoader.Load(Package(), Inputs.AbiHash);
            GpuBufferRef managedBuffer = scope.CreateBuffer(new(64, GpuBufferUsage.Storage | GpuBufferUsage.CopySource));
            GpuTextureRef managedTexture = scope.CreateTexture(new(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1,
                GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
            GpuViewRef managedView = scope.GetView(managedTexture);
            var samplerDescription = new GpuSamplerDescription(AddressU: GpuSamplerAddressMode.Repeat);
            GpuSamplerRef sampler = scope.GetSampler(samplerDescription);
            PortablePassBuffer buffer = context.ImportBuffer(managedBuffer), target = context.ImportBuffer(request);
            PortablePassTexture texture = context.ImportTexture(managedTexture);
            PortablePassView view = context.CreateView("sampled", texture);
            var inputs = new Inputs(buffer, view, sampler) { writerOffset = offset, writerLength = length };
            PortablePassBindings bindings = context.CreateBindings("generated", program, Inputs.Group, inputs);
            ulong copyLength = length == ulong.MaxValue ? 64 - offset : length;
            Expected =
            [
                GpuBindingEntry.Texture(2, services.Resources.GetTextureView(managedView)),
                GpuBindingEntry.Buffer(5, services.Resources.GetBufferRange(managedBuffer, offset, copyLength)),
                GpuBindingEntry.Sampler(8, samplerDescription),
            ];
            context.AddPass("use generated bindings", (bindings, buffer, target, offset, copyLength), static (record, state) =>
            {
                record.Commands.BeginCompute();
                record.Commands.SetComputeBindings(Inputs.Group, record.GetBindings(state.bindings));
                record.Commands.EndCompute();
                record.Commands.CopyBuffer(record.GetBufferRange(state.buffer, state.offset, state.copyLength),
                    record.GetBufferRange(state.target, 0, state.copyLength));
            }).Read(buffer, PortablePassUsage.StorageRead).Read(buffer, PortablePassUsage.CopySource)
                .Read(texture, PortablePassUsage.SampledRead).Write(target, PortablePassUsage.CopyDestination);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        { scope?.Dispose(); program?.Dispose(); return ValueTask.CompletedTask; }
    }

    private static PortableShaderPackage Package() => new(PortableShaderPackage.CurrentVersion,
        "@compute @workgroup_size(1) fn main() {}", [new(GpuShaderStage.Compute, "main")], PortableShaderFeatures.None,
        [new([new(5, GpuShaderStage.Compute, new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
            new(2, GpuShaderStage.Compute, new GpuTextureBindingLayout(GpuTextureSampleType.Float)),
            new(8, GpuShaderStage.Compute, new GpuSamplerBindingLayout(GpuSamplerBindingType.Filtering))])],
        null, [], new([new("writer", 0, 5, GpuBindingLayoutKind.Buffer),
            new("event", 0, 2, GpuBindingLayoutKind.Texture), new("sampler", 0, 8, GpuBindingLayoutKind.Sampler)]), Inputs.AbiHash);
}
