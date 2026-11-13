using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Declarations are live ranges; only an explicit CPU transfer captures application values.</summary>
public sealed class ExplicitTransferBoundaryTests
{
    private const string Source = """
        #include "lumyte.slang"
        struct ExplicitTransferValue
        {
            uint Value;
            GpuBufferRef<uint> Target;
        };
        struct ExplicitTransferRoot
        {
            GpuBufferRef<uint> Numeric;
            GpuBufferRef<ExplicitTransferValue> Typed;
            GpuRWBufferRef<uint> Output;
        };
        ConstantBuffer<ExplicitTransferRoot> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            ExplicitTransferValue value = arguments.Typed.Load(0);
            arguments.Output.Store(0, arguments.Numeric.Load(0));
            arguments.Output.Store(1, arguments.Numeric.Load(1));
            arguments.Output.Store(2, value.Value);
            arguments.Output.Store(3, value.Target.Load(0));
        }
        """;

    /// <summary>Generated capture owns numeric bytes even when its source is a mutable class.</summary>
    [Fact]
    public void GeneratedClassSnapshotsOwnPrivateNumericBacking()
    {
        var value = new ExplicitTransferValue { Value = 7 };
        ShaderValueSnapshot before = IShaderArguments.Capture(in value);
        value.Value = 999;
        ShaderValueSnapshot after = IShaderArguments.Capture(in value);
        Assert.Equal(7u, MemoryMarshal.Read<uint>(before.Data.Span));
        Assert.Equal(999u, MemoryMarshal.Read<uint>(after.Data.Span));
        Assert.Equal(20, before.Data.Length);
        Assert.Contains(before.GetDiagnosticFields(), field => field.Path == "Target" && field.Offset == 4 && field.IsReference);
    }

    /// <summary>Vulkan observes mutations only at explicit CopyFrom boundaries, in both validation modes.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task VulkanDeclarationsStayLiveUntilExplicitTransferAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact);
        }
    }

    /// <summary>Wgpu observes mutations only at explicit CopyFrom boundaries, in both validation modes.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task WgpuDeclarationsStayLiveUntilExplicitTransferAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact);
        }
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target)
        => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target });

    private static async Task CheckAsync(IGraphicDevice device, ShaderArtifact artifact)
    {
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsBuffer<uint> numericUpload = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> numericGpu = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead, Memory = MemoryPreference.Automatic });
        using IGraphicsShaderDataBuffer<ExplicitTransferValue> typedUpload = device.CreateShaderDataBuffer<ExplicitTransferValue>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<ExplicitTransferValue> typedGpu = device.CreateShaderDataBuffer<ExplicitTransferValue>(artifact, 1, MemoryPreference.Automatic);
        using IGraphicsBuffer<uint> targetUpload = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> targetA = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead, Memory = MemoryPreference.Automatic });
        using IGraphicsBuffer<uint> targetB = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead, Memory = MemoryPreference.Automatic });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource, Memory = MemoryPreference.Automatic });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 5 });

        // All source/destination slices and table registrations precede the application mutations.
        BufferSlice<uint> numericSource = numericUpload.SliceElements(0, 2);
        BufferSlice<uint> numericDestination = numericGpu.SliceElements(0, 2);
        ShaderDataSlice<ExplicitTransferValue> typedSource = typedUpload.SliceElements(0, 1);
        ShaderDataSlice<ExplicitTransferValue> typedDestination = typedGpu.SliceElements(0, 1);
        BufferSlice<uint> outputRange = output.SliceElements(0, 4);
        BufferSlice<uint> readbackRange = readback.SliceElements(0, 4);
        IGpuRef<uint> a = table.WriteBuffer(0, targetA.SliceElements(0, 1), ShaderBufferAccess.ReadOnly);
        IGpuRef<uint> b = table.WriteBuffer(1, targetB.SliceElements(0, 1), ShaderBufferAccess.ReadOnly);
        var root = new ExplicitTransferRoot
        {
            Numeric = table.WriteBuffer(2, numericDestination, ShaderBufferAccess.ReadOnly),
            Typed = table.WriteBuffer(3, typedDestination),
            Output = table.WriteBuffer(4, outputRange, ShaderBufferAccess.ReadWrite),
        };
        uint[] numbers = [1, 2];
        ExplicitTransferValue[] values = [new() { Value = 1, Target = a }];

        await targetUpload.MapAsync();
        targetUpload.CopyFrom(new uint[] { 100, 200 });
        targetUpload.Unmap();
        using (IGraphicsCommandBuffer initialize = device.CreateCommandBuffer(new()))
        {
            initialize.Barrier(new BufferBarrierDesc<uint> { Buffer = targetUpload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            initialize.CopyBuffer(targetUpload.SliceElements(0, 1), targetA.SliceElements(0, 1));
            initialize.CopyBuffer(targetUpload.SliceElements(1, 1), targetB.SliceElements(0, 1));
            initialize.Barrier(new BufferBarrierDesc<uint> { Buffer = targetA.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
            initialize.Barrier(new BufferBarrierDesc<uint> { Buffer = targetB.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
            initialize.Finish();
            using IGraphicsSubmission initialized = device.Queue.Submit([initialize]);
            await initialized.WaitAsync();
        }

        numbers[0] = 7;
        numbers[1] = 8;
        values[0].Value = 7;
        values[0].Target = b;
        await MapAndCopyAsync();

        // CPU mutation after CopyFrom must affect neither mapped bytes nor reference metadata.
        numbers[0] = 999;
        numbers[1] = 1000;
        values[0].Value = 999;
        values[0].Target = a;
        numericUpload.Unmap();
        typedUpload.Unmap();
        await DispatchAndCheckAsync([7, 8, 7, 200], reuse: false);

        // A new explicit CPU transfer and GPU copy make the changed application state visible.
        await MapAndCopyAsync();
        numericUpload.Unmap();
        typedUpload.Unmap();
        await DispatchAndCheckAsync([999, 1000, 999, 100], reuse: true);

        // Multiple writes while mapped, before recording CopyBuffer, are legal: the last wins.
        await MapAndCopyAsync();
        numbers[0] = 11;
        numbers[1] = 12;
        values[0].Value = 13;
        values[0].Target = b;
        numericSource.CopyFrom(numbers);
        typedUpload.CopyFrom(values);
        numericUpload.Unmap();
        typedUpload.Unmap();
        await DispatchAndCheckAsync([11, 12, 13, 200], reuse: true);

        async Task MapAndCopyAsync()
        {
            // The preceding submission is always retired before staging is mapped again.
            await numericUpload.MapAsync();
            await typedUpload.MapAsync();
            numericSource.CopyFrom(numbers);
            typedUpload.CopyFrom(values);
        }

        async Task DispatchAndCheckAsync(uint[] expected, bool reuse)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            if (reuse)
            {
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = numericDestination, Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
                commands.Barrier(new ShaderDataBufferBarrierDesc<ExplicitTransferValue> { Buffer = typedDestination, Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = outputRange, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readbackRange, Before = new(PipelineStage.Host, ResourceAccess.HostRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            }

            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = numericSource, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.Barrier(new ShaderDataBufferBarrierDesc<ExplicitTransferValue> { Buffer = typedSource, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(numericSource, numericDestination);
            commands.CopyBuffer(typedSource, typedDestination);
            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = numericDestination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
            commands.Barrier(new ShaderDataBufferBarrierDesc<ExplicitTransferValue> { Buffer = typedDestination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
            IComputeEncoder compute = commands.BeginComputePass(new());
            try
            {
                compute.SetPipeline(pipeline);
                compute.SetArgumentTable(table);
                compute.SetArguments(in root);
                compute.Dispatch(1);
            }
            finally
            {
                compute.End();
            }

            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = outputRange, Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(outputRange, readbackRange);
            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readbackRange, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            await readback.MapAsync();
            uint[] actual = new uint[4];
            readbackRange.CopyTo(actual);
            readback.Unmap();
            Assert.Equal(expected, actual);
        }
    }
}
