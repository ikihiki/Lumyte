using System.Runtime.InteropServices;
using Lumyte.Graphics.Portable.Shaders;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static PortableShaderPackage RootWriterPackage(string source) => new(
        PortableShaderPackage.CurrentVersion,
        source,
        [new(P.GpuShaderStage.Compute, "main")],
        PortableShaderFeatures.ImmediateAddressSpace,
        [new([new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage))])],
        new("Root", 8, 4, [new("index", "u32", 0, 4, 4), new("value", "u32", 4, 4, 4)]),
        [],
        new([new("Output", 0, 0, P.GpuBindingLayoutKind.Buffer)]),
        "browser-root-writer-v1");

    private static async Task<object> ShaderPackageAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        PortableShaderProgram program = fixture.LoadShader(RootWriterPackage(RootWriter));
        P.GpuBindingsHandle bindings = fixture.Bindings(program.BindingLayouts[0], output);
        P.GpuComputePipelineHandle pipeline = fixture.Compute(program);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.SetComputeRootData(new PackageRoot(0, 41));
        commands.Dispatch(1);
        commands.SetComputeRootData(new PackageRoot(1, 89));
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAsync(commands);
        byte[] actual = await fixture.ReadAsync(readback, 8);
        return new { actual = MemoryMarshal.Cast<byte, uint>(actual).ToArray() };
    }

    private static async Task<object> InvalidShaderPackageAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage);
        PortableShaderProgram program = fixture.LoadShader(RootWriterPackage("this is not WGSL"));
        P.GpuBindingsHandle bindings = fixture.Bindings(program.BindingLayouts[0], output);
        P.GpuComputePipelineHandle pipeline = fixture.Compute(program);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.SetComputeRootData(new PackageRoot(0, 41));
        commands.Dispatch(1);
        commands.EndCompute();
        P.GpuSemaphore completion = fixture.Semaphore();
        fixture.Queue.Submit([commands], completion, 1);
        try
        {
            await fixture.Queue.WaitAsync(completion, 1);
            throw new InvalidOperationException("The runtime accepted the invalid shader package.");
        }
        catch (P.GpuExecutionException error)
        {
            return new { complete = fixture.Queue.IsComplete(completion, 1), kinds = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray() };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PackageRoot(uint Index, uint Value);
}
