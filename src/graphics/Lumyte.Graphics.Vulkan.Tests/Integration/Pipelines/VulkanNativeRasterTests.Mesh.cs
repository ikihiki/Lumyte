using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeRasterTests
{
    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshAndPixelConsumeDirectRootSnapshots()
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.MeshPipeline();
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        byte[] root = Root(positions.GpuAddress, new(0, 1, 0, 1), tail: 31);
        commands.SetScissor(new(0, 0, 4, 8));
        commands.DispatchMesh(root, 1);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(20), 0);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(24), 1);
        commands.SetScissor(new(4, 0, 4, 8));
        commands.DispatchMesh(root, 1);
        root.AsSpan().Fill(0xFF);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 31, 255, 0, 255 }, Pixel(readback, 2, 4));
        Assert.Equal(new byte[] { 31, 0, 255, 255 }, Pixel(readback, 6, 4));
    }

    [VulkanMeshTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshGroupCountsReachAllThreeAxes(bool indirect)
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.MeshPipeline(meshFile: "NativeGroupMesh.spv", meshEntry: "groupMeshMain");
        var producer = r.Compute("NativeMeshArguments.spv", "meshArgumentsMain");
        var positions = r.Positions();
        var arguments = r.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var argumentRange = new NativeGpuRange(arguments, 64, 12);
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        if (indirect)
        {
            commands.SetComputePipeline(producer);
            commands.Dispatch(Root(0, default, arguments: argumentRange.GpuAddress), 1);
            commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.DrawIndirect, GpuAccess.IndirectRead);
        }
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        byte[] root = Root(positions.GpuAddress, new(0, 1, 1, 1));
        if (indirect) { commands.DispatchMeshIndirect(root, argumentRange); }
        else { commands.DispatchMesh(root, 2, 3, 4); }
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 255, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanMeshTheory(amplification: true)]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void AmplificationPayloadAndMeshAndPixelReadTheSameRoot(bool indirect)
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.MeshPipeline(task: true);
        var positions = r.Positions();
        var arguments = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        new uint[] { 1, 1, 1 }.CopyTo(MemoryMarshal.Cast<byte, uint>(Bytes(arguments)[64..]));
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        byte[] root = Root(positions.GpuAddress, new(0, 1, 0, 1), instanceOffset: .5f, tail: 17);
        if (indirect) { commands.DispatchMeshIndirect(root, new(arguments, 64, 12)); }
        else { commands.DispatchMesh(root, 1); }
        root.AsSpan().Fill(0xFF);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(readback, 0, 4));
        Assert.Equal(new byte[] { 17, 255, 0, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshLineTopologyProducesALine()
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.MeshPipeline(description: new()
        {
            Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Line,
            ColorTargets = [new(GpuFormat.Rgba8Unorm)],
        }, meshFile: "NativeLineMesh.spv", meshEntry: "lineMeshMain");
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        commands.DispatchMesh(Root(0, new(0, 1, 0, 1)), 1);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 4, 3));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(readback, 4, 0));
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void VertexAndMeshPipelinesShareDynamicDepthState()
    {
        using var r = new Resources();
        var target = r.Target();
        var depth = r.Target(GpuFormat.D32Float);
        var description = new NativeGpuRasterPipelineDescription
        {
            ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.D32Float,
        };
        var vertex = r.Pipeline(description);
        var mesh = r.MeshPipeline(description: description with { Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle });
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)],
            new(depth.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: 1));
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less));
        commands.SetPipeline(vertex);
        commands.Draw(Root(positions.GpuAddress, new(0, 1, 0, 1), depth: .5f), 3);
        commands.SetPipeline(mesh);
        commands.DispatchMesh(Root(positions.GpuAddress, new(1, 0, 0, 1), depth: .75f), 1);
        commands.SetScissor(new(4, 0, 4, 8));
        commands.DispatchMesh(Root(positions.GpuAddress, new(0, 0, 1, 1), depth: .25f), 1);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 2, 4));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(readback, 6, 4));
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshShaderBytesAreConsumedBeforeCreationReturns()
    {
        using var r = new Resources();
        var target = r.Target();
        byte[] code = ShaderBytes("NativeMesh.spv");
        var pipeline = r.MeshPipeline(meshBytes: code);
        code.AsSpan().Fill(0xFF);
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        commands.DispatchMesh(Root(positions.GpuAddress, new(1, 0, 1, 1)), 1);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 255, 0, 255, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshIndirectCannotReadBeyondItsLogicalRange()
    {
        using var r = new Resources();
        var target = r.Target();
        var arguments = r.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);

        var error = Assert.Throws<ArgumentException>(() => commands.DispatchMeshIndirect([], new(arguments, 64, 8)));

        Assert.Equal("arguments", error.ParamName);
        commands.EndRendering();
    }

    [VulkanMeshTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshDispatchRequiresARenderingScope(bool indirect)
    {
        using var backend = VulkanBackend.Create();
        using var commands = backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (indirect) { commands.DispatchMeshIndirect([], default); }
            else { commands.DispatchMesh([], 1); }
        });

        Assert.Contains("rendering scope", error.Message);
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshPipelineSamplesSelectedHeapsAfterCommandSegmentChanges()
    {
        using var r = new Resources();
        var target = r.Target();
        var sampled = r.Target();
        var pipeline = r.MeshPipeline(pixelFile: "NativeSampledPixel.spv", pixelEntry: "sampledMain");
        var positions = r.Positions();
        var upload = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        for (int i = 0; i < 64; i++) { new byte[] { 37, 113, 211, 255 }.CopyTo(Bytes(upload)[(i * 4)..]); }
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        var heap = r.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        var samplers = r.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 8);
        r.Backend.WriteTextureDescriptor(heap, 5, new(sampled.Texture, NativeGpuTextureViewDimension.TwoD,
            GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
        r.Backend.WriteSamplerDescriptor(samplers, 2, new());
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.SetSamplerDescriptorHeap(samplers);
        commands.CopyMemoryToTexture(new(upload, 0, 256), sampled.Texture,
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.PixelShader, GpuAccess.ShaderRead);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);
        commands.DispatchMesh(Root(positions.GpuAddress, default, texture: 5, sampler: 2), 1);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 37, 113, 211, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MeshPipelineCanRenderDepthWithoutAPixelShader()
    {
        using var r = new Resources();
        var depth = r.Target(GpuFormat.D32Float);
        var pipeline = r.MeshPipeline(description: new()
        {
            Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle, DepthStencilFormat = GpuFormat.D32Float,
        }, pixelFile: null);
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([], new(depth.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: 1));
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Always));
        commands.DispatchMesh(Root(positions.GpuAddress, default, depth: .375f), 1);
        commands.EndRendering();
        ReadDepthStencil(commands, depth.Texture, readback, NativeGpuTextureAspect.Depth);

        r.Submit(commands);

        Assert.Equal(.375f, MemoryMarshal.Cast<byte, float>(Bytes(readback))[36]);
    }

    [VulkanMeshFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DestroyedMeshPipelineCannotBeSelectedAgain()
    {
        using var backend = VulkanBackend.Create();
        var pipeline = backend.CreateRasterPipeline(new()
        {
            Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle, DepthStencilFormat = GpuFormat.D32Float,
        }, new(new NativeGpuShaderCode { Stage = GpuShaderStage.Mesh, Code = ShaderBytes("NativeMesh.spv"), EntryPoint = "meshMain" }));
        backend.DestroyRasterPipeline(pipeline);
        using var commands = backend.MainQueue.StartCommandRecording();

        Assert.Throws<ObjectDisposedException>(() => commands.SetPipeline(pipeline));
    }

    [VulkanMeshTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshProgramRequiresOnlyItsOwnTopology(bool alsoVertexTopology)
    {
        using var backend = VulkanBackend.Create();
        var description = new NativeGpuRasterPipelineDescription
        {
            Topology = alsoVertexTopology ? NativeGpuPrimitiveTopology.TriangleList : null,
            MeshOutputTopology = alsoVertexTopology ? NativeGpuMeshOutputTopology.Triangle : null,
        };
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode { Stage = GpuShaderStage.Mesh, Code = ShaderBytes("NativeMesh.spv"), EntryPoint = "meshMain" });

        var error = Assert.Throws<ArgumentException>(() => backend.CreateRasterPipeline(description, program));

        Assert.Equal("description", error.ParamName);
    }

    private sealed partial class Resources
    {
        public NativeGpuRasterPipelineHandle MeshPipeline(bool task = false, NativeGpuRasterPipelineDescription? description = null,
            string meshFile = "NativeMesh.spv", string meshEntry = "meshMain", byte[]? meshBytes = null,
            string? pixelFile = "NativeMeshPixel.spv", string pixelEntry = "meshPixelMain")
        {
            List<NativeGpuShaderCode> stages = [new()
            {
                Stage = GpuShaderStage.Mesh, Code = meshBytes ?? ShaderBytes(task ? "NativePayloadMesh.spv" : meshFile),
                EntryPoint = task ? "payloadMeshMain" : meshEntry,
            }];
            if (task) { stages.Add(new() { Stage = GpuShaderStage.Amplification, Code = ShaderBytes("NativeTask.spv"), EntryPoint = "taskMain" }); }
            if (pixelFile is not null) { stages.Add(new() { Stage = GpuShaderStage.Pixel, Code = ShaderBytes(pixelFile), EntryPoint = pixelEntry }); }
            var pipeline = Backend.CreateRasterPipeline(description ?? new()
            {
                Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle,
                ColorTargets = [new(GpuFormat.Rgba8Unorm)],
            }, new(stages.ToArray()));
            pipelines.Add(pipeline);
            return pipeline;
        }
    }
}

internal sealed class VulkanMeshFactAttribute : FactAttribute
{
    public VulkanMeshFactAttribute(bool amplification = false) => Skip = VulkanMeshSupport.UnavailableReason(amplification);
}

internal sealed class VulkanMeshTheoryAttribute : TheoryAttribute
{
    public VulkanMeshTheoryAttribute(bool amplification = false) => Skip = VulkanMeshSupport.UnavailableReason(amplification);
}

internal static class VulkanMeshSupport
{
    public static string? UnavailableReason(bool amplification)
    {
        try
        {
            using var backend = VulkanBackend.Create();
            if (!backend.Capabilities.MeshShaders) { return "Vulkan VK_EXT_mesh_shader meshShader is unavailable."; }
            return amplification && !backend.Capabilities.AmplificationShaders ? "Vulkan taskShader is unavailable." : null;
        }
        catch (NotSupportedException exception) { return exception.Message; }
    }
}
