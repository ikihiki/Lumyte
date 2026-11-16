using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Real Vulkan model preparation and command recording; measured commands are discarded without submission.</summary>
public sealed class NativeModelWorkload : IDisposable
{
    private readonly ShaderArtifact _artifact = DrawingShaders.LoadModelVertex();
    private readonly IGraphicsShader _vertex;
    private readonly IGraphicsShader _fragment;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsTexture _target;
    private readonly IGraphicsTextureView _targetView;
    private readonly IGraphicsTexture _sample;
    private readonly IGraphicsTextureView _sampleView;
    private readonly IGraphicsSampler _sampler;
    private readonly IArgumentTable _table;
    private readonly IGraphicsShaderDataBuffer<ModelDrawArguments> _draw;
    private readonly ModelMesh _mesh;
    private readonly SampledTexture _sampled;
    private readonly IGpuRef<ModelVertex> _vertices;
    private readonly IGraphicsShaderDataBuffer<ModelDrawArguments> _drawUpload;
    private readonly ModelDrawArguments[] _arguments = new ModelDrawArguments[32];
    private readonly IGraphicsTexture[] _extraTargets;
    private readonly IGraphicsTextureView[] _extraViews;
    private readonly IGraphicsTexture _depth;
    private readonly IGraphicsTextureView _depthView;
    private readonly IGpuRef<ModelDrawArguments>[] _stableDraws;
    private int _frame;

    /// <summary>Initializes a new instance of the <see cref="NativeModelWorkload"/> class.</summary>
    public NativeModelWorkload()
    {
        _ = HardwareVulkan.Verify();
        Device = VulkanDevice.Create(physicalDeviceIndex: 0);
        _vertex = Device.CreateShader(_artifact);
        _fragment = Device.CreateShader(DrawingShaders.LoadModelFragment());
        _pipeline = Device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
        _target = Device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        _targetView = _target.CreateView();
        _extraTargets = Enumerable.Range(0, 7).Select(_ => Device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource })).ToArray();
        _extraViews = _extraTargets.Select(t => t.CreateView()).ToArray();
        _depth = Device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Depth32Float, Usage = TextureUsage.RenderAttachment });
        _depthView = _depth.CreateView();
        _sample = Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        _sampleView = _sample.CreateView();
        using (IGraphicsBuffer<byte> upload = Device.CreateBuffer<byte>(new() { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }))
        {
            upload.MapAsync().GetAwaiter().GetResult();
            upload.CopyFrom([255, 255, 255, 255]);
            upload.Unmap();
            using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
            commands.Barrier(new TextureBarrierDesc { Texture = _sample, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            commands.CopyBufferToTexture(new() { Buffer = upload.SliceElements(0, 4), BytesPerRow = 4, RowsPerImage = 1 }, new() { Texture = _sample, Width = 1, Height = 1 });
            commands.Barrier(new TextureBarrierDesc { Texture = _sample, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
            commands.Finish();
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            submission.WaitAsync().GetAwaiter().GetResult();
        }

        _sampler = Device.CreateSampler(new());
        _table = Device.CreateArgumentTable(new() { BufferCapacity = 2, TextureCapacity = 1, SamplerCapacity = 1 });
        _sampled = new(_table.WriteTexture(0, _sampleView), _table.WriteSampler(0, _sampler));
        _mesh = CreateMesh(false);
        Upload(_mesh);
        _vertices = _table.WriteBuffer(0, _mesh.Vertices.SliceElements(0, _mesh.Vertices.Count));
        _draw = Device.CreateShaderDataBuffer<ModelDrawArguments>(_artifact, 32);
        _drawUpload = Device.CreateShaderDataBuffer<ModelDrawArguments>(_artifact, 32, MemoryPreference.Upload);
        _stableDraws = new IGpuRef<ModelDrawArguments>[32];
        PrepareUnchangedRegistrations();
    }

    /// <summary>Gets deterministic recording observations.</summary>
    public VulkanDevice Device { get; }

    /// <summary>Restores one full-range registration before unchanged-registration measurements.</summary>
    public void PrepareUnchangedRegistrations()
    {
        IGpuRef<ModelDrawArguments> allDraws = _table.WriteBuffer(1, _draw.SliceElements(0, 32));
        for (int i = 0; i < _stableDraws.Length; i++)
        {
            _stableDraws[i] = allDraws.GetElement((ulong)i);
        }
    }

    /// <summary>Allocates, bulk-packs, records upload, simulates completion and releases storage.</summary>
    /// <param name="packed">Whether to prepare packed CPU bytes.</param>
    /// <returns>The consumed value or operation observation.</returns>
    public ulong CreateUploadDispose(bool packed)
    {
        using ModelMesh mesh = CreateMesh(packed);
        Upload(mesh);
        return mesh.IndexCount;
    }

    /// <summary>Updates transforms/material buffer and records indexed draws sharing one mesh.</summary>
    /// <param name="instances">The number of shared-mesh draw records.</param>
    /// <param name="complete">Whether untimed verification submits and waits.</param>
    /// <param name="scenario">Numeric, Static, Mutation, CrossPass or Unchanged recording.</param>
    /// <param name="targetCount">Color attachments from one to eight; zero is state-only.</param>
    /// <returns>The consumed value or operation observation.</returns>
    public ulong RecordInstances(int instances, bool complete = false, string scenario = "Numeric", int targetCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(instances, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(instances, 32);
        ValidateTargets(targetCount);
        if (targetCount == 0)
        {
            throw new NotSupportedException("Zero color targets are covered by state/key checks, not the model recording oracle.");
        }

        if (scenario == "CrossPass" && targetCount != 1)
        {
            throw new NotSupportedException("CrossPass currently supports one color attachment.");
        }

        int frame = scenario == "Static" ? 0 : ++_frame;
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        for (int index = 0; index < instances; index++)
        {
            Matrix4x4 world = Matrix4x4.CreateScale(1, 2, 1) * Matrix4x4.CreateTranslation(index * 2, (frame % 2) * 0.001f, 0);
            ModelMaterial material = ModelDrawing.DefaultMaterial with { AlphaMode = (uint)(index % 3), Roughness = 0.4f, EmissiveStrength = 2, Unlit = 1, BaseColor = new(1, 0, 0, 1) };
            _arguments[index] = ModelDrawing.CreateArguments(_vertices, material, _sampled, _sampled) with
            {
                World = world,
                NormalTransform = ModelDrawing.CreateNormalTransform(world),
            };
        }

        _drawUpload.MapAsync().GetAwaiter().GetResult();
        _drawUpload.CopyFrom(_arguments.AsSpan(0, instances));
        _drawUpload.Unmap();
        ShaderDataSlice<ModelDrawArguments> source = _drawUpload.SliceElements(0, (ulong)instances);
        ShaderDataSlice<ModelDrawArguments> destination = _draw.SliceElements(0, (ulong)instances);
        commands.Barrier(new ShaderDataBufferBarrierDesc<ModelDrawArguments> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source, destination);
        commands.Barrier(new ShaderDataBufferBarrierDesc<ModelDrawArguments> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = _target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        for (int i = 1; i < targetCount; i++)
        {
            commands.Barrier(new TextureBarrierDesc { Texture = _extraTargets[i - 1], Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        }

        IRenderEncoder render = commands.BeginRenderPass(Pass(targetCount));
        try
        {
            render.SetPipeline(_pipeline);
            render.SetViewport(new(0, 0, 64, 64, 0, 1));
            render.SetScissor(new(0, 0, 64, 64));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            render.SetArgumentTable(_table);
            for (int index = 0; index < instances; index++)
            {
                if (scenario == "Mutation")
                {
                    _table.WriteTexture(0, _sampleView);
                    _table.WriteSampler(0, _sampler);
                    _table.WriteTexture(0, _sampleView);
                }

                if (scenario == "CrossPass" && index > 0)
                {
                    render.End();
                    render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _targetView, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
                    render.SetPipeline(_pipeline);
                    render.SetViewport(new(0, 0, 64, 64, 0, 1));
                    render.SetScissor(new(0, 0, 64, 64));
                    render.SetBlendConstant(new(0, 0, 0, 0));
                    render.SetStencilReference(0);
                    render.SetArgumentTable(_table);
                }

                render.SetRenderState(State(ModelDrawing.CreateRenderState(_arguments[index].Material, doubleSided: index % 2 == 0) with { DepthStencil = new() }, targetCount));
                var root = new ModelDrawRoot(scenario == "Unchanged" ? _stableDraws[index] : _table.WriteBuffer(1, _draw.SliceElements((ulong)index, 1)));
                render.SetArguments(in root);
                _mesh.Draw(render);
            }
        }
        finally
        {
            render.End();
        }

        commands.Finish();
        if (complete)
        {
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            submission.WaitAsync().GetAwaiter().GetResult();
        }

        return (ulong)instances;
    }

    /// <summary>Submits a real model then checks an analytic center red pixel outside timing.</summary>
    /// <param name="targetCount">Color attachments from one to eight. Output zero is red; other outputs retain their transparent clear.</param>
    /// <param name="scenario">The exact recording scenario to submit outside timing.</param>
    public void Verify(int targetCount = 1, string scenario = "Numeric")
    {
        ValidateTargets(targetCount);
        RecordInstances(32, complete: true, scenario: scenario, targetCount: targetCount);
        CheckPixel(_target, true);
        for (int i = 1; i < targetCount; i++)
        {
            CheckPixel(_extraTargets[i - 1], false);
        }
    }

    /// <summary>Measures real backend state validation/snapshot preparation without drawing or submission.</summary>
    /// <param name="count">State count.</param>
    /// <param name="targetCount">Color attachments; zero selects a depth-only pass.</param>
    /// <returns>State operations performed.</returns>
    public int States(int count, int targetCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateTargets(targetCount);
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        IRenderEncoder render = commands.BeginRenderPass(Pass(targetCount));
        for (int i = 0; i < count; i++)
        {
            render.SetRenderState(State(ModelDrawing.CreateRenderState(ModelDrawing.DefaultMaterial with { AlphaMode = (uint)(i % 3) }, i % 2 == 0), targetCount));
        }

        render.End();
        commands.Finish();
        return count;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _depthView.Dispose();
        _depth.Dispose();
        foreach (IGraphicsTextureView view in _extraViews)
        {
            view.Dispose();
        }

        foreach (IGraphicsTexture texture in _extraTargets)
        {
            texture.Dispose();
        }

        _table.Dispose();
        _draw.Dispose();
        _drawUpload.Dispose();
        _mesh.Dispose();
        _sampler.Dispose();
        _sampleView.Dispose();
        _sample.Dispose();
        _targetView.Dispose();
        _target.Dispose();
        _pipeline.Dispose();
        _fragment.Dispose();
        _vertex.Dispose();
        Device.Dispose();
    }

    /// <summary>Creates reusable typed source vertices.</summary>
    private static ModelVertex[] Vertices() =>
    [
        new(new(-0.5f, -0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0, 1), Vector4.One),
        new(new(0.5f, -0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(1, 1), Vector4.One),
        new(new(0, 0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0.5f, 0), Vector4.One),
    ];

    private static void ValidateTargets(int targets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targets);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targets, 8);
    }

    private static GraphicsRenderStateDesc State(GraphicsRenderStateDesc state, int targets) => targets == 1 ? state : state with { ColorTargets = Enumerable.Range(0, targets).Select(i => i == 0 ? state.ColorTargets[0] : state.ColorTargets[0] with { WriteMask = ColorWriteMask.None }).ToArray() };

    private void CheckPixel(IGraphicsTexture target, bool red)
    {
        using IGraphicsBuffer<byte> readback = Device.CreateBuffer<byte>(new() { Count = 64 * 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 64, Height = 64 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = 256, RowsPerImage = 64 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
        submission.WaitAsync().GetAwaiter().GetResult();
        readback.MapAsync().GetAwaiter().GetResult();
        byte[] bytes = new byte[64 * 256];
        readback.CopyTo(bytes);
        readback.Unmap();
        int center = (32 * 256) + (32 * 4);
        byte channel = red ? (byte)255 : (byte)0;
        if (bytes[center] != channel || bytes[center + 1] != 0 || bytes[center + 2] != 0 || bytes[center + 3] != channel)
        {
            throw new InvalidOperationException("Native model output zero must be red; write-masked outputs must retain transparent clear.");
        }
    }

    private RenderPassDesc Pass(int targets) => new()
    {
        ColorAttachments = Enumerable.Range(0, targets).Select(i => new RenderColorAttachmentDesc { View = i == 0 ? _targetView : _extraViews[i - 1], LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }).ToArray(),
        DepthStencilAttachment = targets == 0 ? new() { View = _depthView, DepthLoadOp = AttachmentLoadOp.Clear, DepthStoreOp = AttachmentStoreOp.Store } : null,
    };

    private ModelMesh CreateMesh(bool packed)
    {
        ModelVertex[] vertices = Vertices();
        ushort[] indices = [0, 1, 2];
        return packed
            ? ModelMesh.CreatePackedAsync(Device, MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(), indices, _artifact).GetAwaiter().GetResult()
            : ModelMesh.CreateAsync(Device, vertices, indices, _artifact).GetAwaiter().GetResult();
    }

    private void Upload(ModelMesh mesh)
    {
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        mesh.RecordInitialUpload(commands);
        commands.Finish();
        using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
        submission.WaitAsync().GetAwaiter().GetResult();
        mesh.ReleaseUploadResources();
    }
}
