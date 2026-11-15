using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Drawing.Benchmark.Models;

/// <summary>Real model upload and indexed recording against managed storage, without GPU execution.</summary>
public sealed class ModelWorkload : IDisposable
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

    /// <summary>Initializes a new instance of the <see cref="ModelWorkload"/> class.</summary>
    public ModelWorkload()
    {
        Device = new();
        _vertex = Device.CreateShader(_artifact);
        _fragment = Device.CreateShader(DrawingShaders.LoadModelFragment());
        _pipeline = Device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
        _target = Device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        _targetView = _target.CreateView();
        _sample = Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        _sampleView = _sample.CreateView();
        using (IGraphicsBuffer<byte> upload = Device.CreateBuffer<byte>(new() { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }))
        {
            upload.MapAsync().GetAwaiter().GetResult();
            upload.CopyFrom([255, 255, 255, 255]);
            upload.Unmap();
            using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
            commands.CopyBufferToTexture(new() { Buffer = upload.SliceElements(0, 4), BytesPerRow = 4, RowsPerImage = 1 }, new() { Texture = _sample, Width = 1, Height = 1 });
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
    }

    /// <summary>Gets deterministic recording observations.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Allocates, bulk-packs, records upload, simulates completion and releases storage.</summary>
    /// <param name="packed">Whether to prepare packed CPU bytes.</param>
    /// <returns>The consumed value or operation observation.</returns>
    public ulong CreateUploadDispose(bool packed)
    {
        Device.ResetCounters();
        using ModelMesh mesh = CreateMesh(packed);
        Upload(mesh);
        return Device.Counters.Checksum;
    }

    /// <summary>Updates transforms/material buffer and records indexed draws sharing one mesh.</summary>
    /// <param name="instances">The number of shared-mesh draw records.</param>
    /// <returns>The consumed value or operation observation.</returns>
    public ulong RecordInstances(int instances)
    {
        Device.ResetCounters();
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        for (int index = 0; index < instances; index++)
        {
            Matrix4x4 world = Matrix4x4.CreateScale(1, 2, 1) * Matrix4x4.CreateTranslation(index, 0, 0);
            ModelMaterial material = ModelDrawing.DefaultMaterial with { AlphaMode = (uint)(index % 3), Roughness = 0.4f, EmissiveStrength = 2 };
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
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _targetView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(_pipeline);
        render.SetArgumentTable(_table);
        for (int index = 0; index < instances; index++)
        {
            render.SetRenderState(ModelDrawing.CreateRenderState(_arguments[index].Material, doubleSided: index % 2 == 0));
            var root = new ModelDrawRoot(_table.WriteBuffer(1, _draw.SliceElements((ulong)index, 1)));
            render.SetArguments(in root);
            _mesh.Draw(render);
        }

        render.End();
        commands.Finish();
        return Device.Counters.Checksum;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
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
