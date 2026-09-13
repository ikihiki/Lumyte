using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableComputePipelineTests
{
    private const string EmptyCompute = "@compute @workgroup_size(1) fn main() { }";

    [Fact]
    public async Task OnlySubmittedPipelinesAreCreatedAndLaterSubmissionsReuseThem()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        _ = fixture.Pipeline(EmptyCompute, entryPoint: "missing");
        _ = fixture.Pipeline("this unused shader is invalid WGSL");
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(EmptyCompute);
        Assert.Equal(0, fixture.Backend.PipelineStatistics.Creations);

        P.GpuCommandBuffer first = fixture.Record();
        first.BeginCompute();
        first.SetComputePipeline(pipeline);
        first.Dispatch(1);
        first.EndCompute();
        Assert.Equal(0, fixture.Backend.PipelineStatistics.Creations);
        await fixture.SubmitAndWaitAsync(first);
        Assert.Equal(1, fixture.Backend.PipelineStatistics.Creations);

        P.GpuCommandBuffer second = fixture.Record();
        second.BeginCompute();
        second.SetComputePipeline(pipeline);
        second.Dispatch(1);
        second.EndCompute();
        await fixture.SubmitAndWaitAsync(second);

        Assert.Equal(1, fixture.Backend.PipelineStatistics.Creations);
    }

    [Fact]
    public async Task ProgramDescriptionKeepsItsEntriesAndGroupLayoutsAfterCallerArrayChanges()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBindingLayoutHandle unusedLayout = fixture.Layout();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output)));
        P.GpuShaderModuleHandle module = fixture.Module(WebGpuPortableComputeTests.RootWriter);
        P.GpuShaderEntryPoint[] entries = [new(module, P.GpuShaderStage.Compute, "main")];
        P.GpuBindingLayoutHandle[] layouts = [layout];
        var description = new P.GpuShaderProgramDescription(entries, layouts, 8);

        entries[0] = entries[0] with { Name = "missing" };
        layouts[0] = unusedLayout;
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(description);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.SetComputeRootData(new WebGpuPortableComputeTests.RootData { Destination = 0, Value = 2026 });
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 2026, 0 }, await fixture.ReadAsync(readback, 2));
    }

    [Fact]
    public async Task RebindingThePipelinePreservesPreviouslyRecordedRootValues()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output)));
        const string source = """
            requires immediate_address_space;
            struct Root { value: u32, padding: u32 }
            var<immediate> root: Root;
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1)
            fn main(@builtin(global_invocation_id) id: vec3u) { output[id.x] = root.value; }
            """;
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(source, 8, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.SetComputeRootData(MemoryMarshal.AsBytes(new uint[] { 7, 0 }.AsSpan()));
        commands.Dispatch(2);

        commands.SetComputePipeline(pipeline);
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 7, 7 }, await fixture.ReadAsync(readback, 2));
    }
}
