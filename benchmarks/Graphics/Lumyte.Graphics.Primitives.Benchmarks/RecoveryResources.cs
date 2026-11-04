using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;

namespace Lumyte.Graphics.Primitives.Benchmarks;

/// <summary>Owns one bounded frame slot and quarantines uncertain submission lifetimes.</summary>
internal sealed class RecoveryResources : IDisposable
{
    private readonly IGraphicDevice _device;
    private readonly List<IDisposable> _owned = new();
    private readonly List<ModelMesh> _meshes = new();
    private readonly List<IGraphicsCommandBuffer> _commands = new();
    private readonly List<IGraphicsSubmission> _submissions = new();
    private readonly ShaderArtifact _vertexArtifact;
    private readonly IGraphicsShader _vertex;
    private readonly IGraphicsShader _fragment;
    private readonly uint _pitch;
    private readonly IGraphicsPipeline _pipeline;
    private IGraphicsTexture _color = null!;
    private IGraphicsTextureView _colorView = null!;
    private IGraphicsTexture? _depth;
    private IGraphicsTextureView? _depthView;
    private IGraphicsTexture? _white;
    private IGraphicsTextureView? _whiteView;
    private IGraphicsSampler? _sampler;
    private IArgumentTable? _table;
    private IGpuRef<ModelVertex>[] _vertexRefs = Array.Empty<IGpuRef<ModelVertex>>();
    private SampledTexture _sampled;
    private bool _disposed;
    private bool _initialized;
    private bool _needsUpload;
    private bool _pending;
    private bool _unknown;
    private IGraphicsBuffer<byte> _readback = null!;

    private RecoveryResources(IGraphicDevice device)
    {
        Program.Quarantine.Add(this);
        _device = device;
        _vertexArtifact = DrawingShaders.LoadModelVertex();
        ShaderArtifact fragmentArtifact = DrawingShaders.LoadModelFragment();
        ShaderHash = Program.Hash(_vertexArtifact.GetTarget(_device.Caps.ShaderTarget).Code.ToArray().Concat(fragmentArtifact.GetTarget(_device.Caps.ShaderTarget).Code.ToArray()).ToArray());
        _vertex = Own(_device.CreateShader(_vertexArtifact));
        _fragment = Own(_device.CreateShader(fragmentArtifact));
        _pipeline = _device.CreateGraphicsPipeline(new()
        {
            VertexShader = _vertex,
            FragmentShader = _fragment,
        });
        _color = Own(_device.CreateTexture(new()
        {
            Width = Program.Size,
            Height = Program.Size,
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        }));
        _colorView = Own(_color.CreateView());
        TextureCopyLayout layout = _device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm);
        _pitch = (((Program.Size * 4) + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
        _readback = Own(_device.CreateBuffer<byte>(new()
        {
            Count = _pitch * Program.Size,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        }));
        _depth = Own(_device.CreateTexture(new()
        {
            Width = Program.Size,
            Height = Program.Size,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.RenderAttachment,
        }));
        _depthView = Own(_depth.CreateView());

        // Unlit mode never evaluates normal mapping. White is a real texture, explicitly cleared/uploaded on GPU.
        _white = Own(_device.CreateTexture(new()
        {
            Width = 1,
            Height = 1,
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
        }));
        _whiteView = Own(_white.CreateView());
        _sampler = Own(_device.CreateSampler(new()
        {
            AddressU = AddressMode.ClampToEdge,
            AddressV = AddressMode.ClampToEdge,
            LodMaxClamp = 0,
        }));
    }

    internal bool Disposed => _disposed;

    internal int ReleasedChildren { get; private set; }

    internal int DrawCalls { get; private set; }

    internal int CommandCalls { get; private set; }

    internal int SubmitCalls { get; private set; }

    internal string ShaderHash { get; }

    /// <summary>Releases this slot only after all accepted submissions have completed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseFrame();
        _table?.Dispose();
        foreach (ModelMesh mesh in _meshes)
        {
            mesh.Dispose();
            RequireDisposed(mesh.Vertices);
        }

        _pipeline.Dispose();
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        RequireDisposed(_readback);
        ReleasedChildren = _owned.Count + _meshes.Count + 1 + (_table is null ? 0 : 1);
        _disposed = true;
    }

    internal static RecoveryResources Create(IGraphicDevice device)
    {
        var resources = new RecoveryResources(device);
        Program.Quarantine.Remove(resources);
        return resources;
    }

    internal Task PrepareAsync(int frame)
    {
        Program.Require(!_pending && !_unknown && !_disposed, "Recovery resources not available for replacement.");
        return CreateMeshesAsync(frame);
    }

    internal void Encode()
    {
        Program.Require(_commands.Count == 0 && !_pending && !_unknown && !_disposed, "Recovery commands not retired.");
        DrawCalls = 0;
        CommandCalls = 0;
        SubmitCalls = 0;
        IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        _commands.Add(commands);
        CommandCalls++;
        Program.Transition(commands, _color, _initialized ? TextureState.ColorAttachment : TextureState.Undefined, TextureState.ColorAttachment, _initialized ? new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) : default, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        Program.Transition(commands, _depth!, _initialized ? TextureState.DepthStencilAttachment : TextureState.Undefined, TextureState.DepthStencilAttachment, _initialized ? new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite) : default, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite));
        if (!_initialized)
        {
            Program.Transition(commands, _white!, TextureState.Undefined, TextureState.ColorAttachment, default, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
            IRenderEncoder clear = commands.BeginRenderPass(new()
            {
                ColorAttachments =
                [
                    new()
                    {
                        View = _whiteView!,
                        LoadOp = AttachmentLoadOp.Clear,
                        StoreOp = AttachmentStoreOp.Store,
                        ClearValue = new(1, 1, 1, 1),
                    },
                ],
            });
            clear.End();
            Program.Transition(commands, _white!, TextureState.ColorAttachment, TextureState.Sampled, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        }

        if (_needsUpload)
        {
            _meshes[0].RecordInitialUpload(commands);
        }

        IRenderEncoder render = commands.BeginRenderPass(new()
        {
            ColorAttachments =
            [
                new()
                {
                    View = _colorView,
                    LoadOp = AttachmentLoadOp.Clear,
                    StoreOp = AttachmentStoreOp.Store,
                },
            ],
            DepthStencilAttachment = new()
            {
                View = _depthView!,
            },
        });
        render.SetPipeline(_pipeline);
        render.SetDynamicState(new(0, 0, Program.Size, Program.Size, 0, 1), new(0, 0, Program.Size, Program.Size));
        ModelMaterial material = ModelDrawing.DefaultMaterial with
        {
            BaseColor = Vector4.One,
            Unlit = 1,
            AlphaMode = 0,
        };
        ModelDrawArguments arguments = ModelDrawing.CreateArguments(_vertexRefs[0], material, _sampled, _sampled);
        render.SetRenderState(ModelDrawing.CreateRenderState(material, doubleSided: true));
        render.SetArgumentTable(_table!);
        render.SetArguments(in arguments);
        _meshes[0].Draw(render);
        DrawCalls++;
        render.End();
        commands.Finish();
        _initialized = true;
    }

    internal void Submit()
    {
        _pending = true;
        try
        {
            _submissions.Add(_device.Queue.Submit(_commands));
            SubmitCalls++;
        }
        catch
        {
            _unknown = true;
            throw;
        }
    }

    internal async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (!_pending)
        {
            return;
        }

        try
        {
            foreach (IGraphicsSubmission submission in _submissions)
            {
                await submission.WaitAsync(cancellationToken);
            }
        }
        catch
        {
            _unknown = true;
            throw;
        }

        Program.Require(!_unknown, "Submission acceptance remains unknown; quarantine required.");
        _pending = false;
        if (_needsUpload)
        {
            foreach (ModelMesh mesh in _meshes)
            {
                mesh.ReleaseUploadResources();
            }

            _needsUpload = false;
        }
    }

    internal void ReleaseFrame()
    {
        Program.Require(!_pending && !_unknown, "Cannot retire incomplete or unknown submission.");
        foreach (IGraphicsSubmission submission in _submissions)
        {
            submission.Dispose();
        }

        _submissions.Clear();
        foreach (IGraphicsCommandBuffer c in _commands)
        {
            c.Dispose();
        }

        _commands.Clear();
    }

    internal async Task<byte[]> ReadPixelsAsync(CancellationToken cancellationToken = default)
    {
        Program.Require(!_pending && !_unknown, "Readback before completion.");
        IGraphicsCommandBuffer c = _device.CreateCommandBuffer(new());

        // Keep readback owners in the same quarantine boundary if submit fails.
        _commands.Add(c);
        Program.Transition(c, _color, TextureState.ColorAttachment, TextureState.CopySource, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), new(PipelineStage.Copy, ResourceAccess.CopyRead));
        c.CopyTextureToBuffer(
            new()
            {
                Texture = _color,
                Width = Program.Size,
                Height = Program.Size,
            },
            new()
            {
                Buffer = _readback.SliceElements(0, _readback.Count),
                BytesPerRow = _pitch,
                RowsPerImage = Program.Size,
            });
        c.Barrier(new BufferBarrierDesc<byte>
        {
            Buffer = _readback.SliceElements(0, _readback.Count),
            Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
            After = new(PipelineStage.Host, ResourceAccess.HostRead),
        });
        Program.Transition(c, _color, TextureState.CopySource, TextureState.ColorAttachment, new(PipelineStage.Copy, ResourceAccess.CopyRead), new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        c.Finish();
        _pending = true;
        try
        {
            IGraphicsSubmission submission = _device.Queue.Submit([c]);
            _submissions.Add(submission);
            await submission.WaitAsync(cancellationToken);
            _pending = false;
        }
        catch
        {
            _unknown = true;
            throw;
        }

        await _readback.MapAsync(cancellationToken);
        byte[] padded = new byte[checked((int)_readback.Count)];
        _readback.CopyTo(padded);
        _readback.Unmap();
        byte[] pixels = new byte[Program.Size * Program.Size * 4];
        for (int y = 0; y < Program.Size; y++)
        {
            Array.Copy(padded, y * _pitch, pixels, y * Program.Size * 4, Program.Size * 4);
        }

        return pixels;
    }

    internal void ConfirmRetired()
    {
        // Only the Hosting native retirement barrier authorizes clearing uncertain work.
        _pending = false;
        _unknown = false;
    }

    private static void RequireDisposed<T>(IGraphicsBuffer<T> buffer)
        where T : unmanaged
    {
        bool disposed = false;
        try
        {
            // This product validation guard is managed-only, even for a retired native epoch.
            buffer.ValidateByteRange(0, 1);
        }
        catch (ObjectDisposedException)
        {
            disposed = true;
        }

        Program.Require(disposed, "Disposed child buffer still accepted range validation.");
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private async Task CreateMeshesAsync(int frame)
    {
        Program.Require(!_pending && !_unknown, "Mesh generation still in GPU use.");
        _table?.Dispose();
        _table = null;
        foreach (ModelMesh mesh in _meshes)
        {
            mesh.Dispose();
            RequireDisposed(mesh.Vertices);
        }

        _meshes.Clear();
        Vector4 color = frame % 2 == 1 ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
        ModelVertex[] vertices =
        [
            new(new(-1, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, color),
            new(new(3, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, color),
            new(new(-1, 3, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, color),
        ];
        uint[] indices = [0, 1, 2];
        _meshes.Add(await ModelMesh.CreateAsync(_device, vertices, indices, _vertexArtifact));
        _table = _device.CreateArgumentTable(new()
        {
            BufferCapacity = 1,
            TextureCapacity = 1,
            SamplerCapacity = 1,
        });
        _vertexRefs = [_table.WriteBuffer(0, _meshes[0].Vertices.SliceElements(0, _meshes[0].Vertices.Count))];
        _sampled = new(_table.WriteTexture(0, _whiteView!), _table.WriteSampler(0, _sampler!));
        _needsUpload = true;
    }
}
