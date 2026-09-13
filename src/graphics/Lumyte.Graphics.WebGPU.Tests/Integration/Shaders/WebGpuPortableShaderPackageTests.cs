using Lumyte.Graphics.Portable.Shaders;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableShaderPackageTests
{
    [Fact]
    public async Task LoadedPackageSuppliesTheRootAndBindingLayoutUsedByCompute()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        var package = new PortableShaderPackage(PortableShaderPackage.CurrentVersion, WebGpuPortableComputeTests.RootWriter,
            [new(P.GpuShaderStage.Compute, "main")], PortableShaderFeatures.ImmediateAddressSpace,
            [new PortableShaderGroupLayout([
                new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage))
            ])], new PortableShaderDataLayout("Root", 8, 4, [
                new("destination", "u32", 0, 4, 4), new("value", "u32", 4, 4, 4)
            ]), [], new PortableShaderBindingSchema([new("output", 0, 0, P.GpuBindingLayoutKind.Buffer)]),
            "portable-package-compute-root-v1");
        using PortableShaderProgram program = new PortableShaderLoader(fixture.Backend).Load(package);
        P.GpuComputePipelineHandle pipeline = fixture.Backend.CreateComputePipeline(program.Description);
        P.GpuBindingsHandle? bindings = null;
        try
        {
            bindings = fixture.Backend.CreateBindings(program.BindingLayouts[0], [P.GpuBindingEntry.Buffer(0, new(output))]);
            using P.GpuCommandBuffer commands = fixture.Record();
            commands.BeginCompute();
            commands.SetComputePipeline(pipeline);
            commands.SetComputeBindings(0, bindings);
            commands.SetComputeRootData(new WebGpuPortableComputeTests.RootData { Destination = 0, Value = 42 });
            commands.Dispatch(1);
            commands.SetComputeRootData(new WebGpuPortableComputeTests.RootData { Destination = 1, Value = 73 });
            commands.Dispatch(1);
            commands.EndCompute();
            commands.CopyBuffer(new(output), new(readback));

            await fixture.SubmitAndWaitAsync(commands);

            Assert.Equal(new uint[] { 42, 73 }, await fixture.ReadAsync(readback, 2));
        }
        finally
        {
            fixture.Backend.DestroyComputePipeline(pipeline);
            if (bindings is not null) { fixture.Backend.DestroyBindings(bindings); }
        }
    }

    [Fact]
    public async Task LoadedInvalidWgslIsDiagnosedByTheSubmittedRuntimeWork()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        var package = new PortableShaderPackage(PortableShaderPackage.CurrentVersion, "invalid WGSL",
            [new(P.GpuShaderStage.Compute, "main")], PortableShaderFeatures.None, [], null, [],
            new PortableShaderBindingSchema([]), "portable-package-invalid-module-v1");
        using PortableShaderProgram program = new PortableShaderLoader(fixture.Backend).Load(package);
        P.GpuComputePipelineHandle pipeline = fixture.Backend.CreateComputePipeline(program.Description);
        try
        {
            using P.GpuCommandBuffer commands = fixture.Record();
            commands.BeginCompute();
            commands.SetComputePipeline(pipeline);
            commands.Dispatch(1);
            commands.EndCompute();
            P.GpuSemaphore completion = fixture.Semaphore();

            fixture.Queue.Submit([commands], completion, 1);
            P.GpuExecutionException error = await Assert.ThrowsAsync<P.GpuExecutionException>(async () => await fixture.Queue.WaitAsync(completion, 1));

            Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Kind == P.GpuDiagnosticKind.Validation);
            Assert.Equal(new P.GpuFenceValue(completion, 1), error.FenceValue);
            Assert.True(fixture.Queue.IsComplete(completion, 1));
        }
        finally { fixture.Backend.DestroyComputePipeline(pipeline); }
    }
}
