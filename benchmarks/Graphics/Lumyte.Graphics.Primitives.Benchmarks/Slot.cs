using System.Diagnostics;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Primitives.Benchmarks;

/// <summary>Owns one bounded frame slot and quarantines uncertain submission lifetimes.</summary>
internal sealed class Slot : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly Program.Case _spec;
    private readonly List<IDisposable> _owned = new();
    private readonly List<ModelMesh> _meshes = new();
    private readonly List<IGraphicsCommandBuffer> _commands = new();
    private readonly List<IGraphicsSubmission> _submissions = new();
    private readonly ShaderArtifact _vertexArtifact;
    private readonly IGraphicsShader _vertex;
    private readonly IGraphicsShader _fragment;
    private readonly uint _pitch;
    private IGraphicsPipeline _pipeline;
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
    private bool _initialized;
    private bool _needsUpload;
    private bool _pending;
    private bool _unknown;
    private long _submitStart;
    private IGraphicsBuffer<byte> _readback = null!;

    private Slot(VulkanDevice device, Program.Case spec)
    {
        _device = device;
        _spec = spec;
        _vertexArtifact = _spec.Model ? DrawingShaders.LoadModelVertex() : ShaderArtifact.LoadEmbedded(typeof(PipelineBenchmark).Assembly, "Lumyte.Shaders.fullscreen-vertex.lshader");
        ShaderArtifact fragmentArtifact = _spec.Model ? DrawingShaders.LoadModelFragment() : ShaderArtifact.LoadEmbedded(typeof(PipelineBenchmark).Assembly, "Lumyte.Shaders.color-fragment.lshader");
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
        if (_spec.Model)
        {
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
    }

    internal int Frame { get; set; } = -1;

    internal double[] CompletionSamples { get; set; } = Array.Empty<double>();

    internal int DrawCalls { get; private set; }

    internal int CommandCalls { get; private set; }

    internal int SubmitCalls { get; private set; }

    internal string ShaderHash { get; }

    /// <summary>Releases this slot only after all accepted submissions have completed.</summary>
    public void Dispose()
    {
        ReleaseFrame();
        _table?.Dispose();
        foreach (ModelMesh mesh in _meshes)
        {
            mesh.Dispose();
        }

        _pipeline.Dispose();
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }
    }

    internal static async Task<Slot> CreateAsync(VulkanDevice device, Program.Case spec)
    {
        var slot = new Slot(device, spec);

        // Resident mesh creation is intentionally outside warm measurement.
        if (spec.Model && !spec.Replace)
        {
            await slot.CreateMeshesAsync(0);
        }

        return slot;
    }

    internal async Task PrepareAsync(int frame)
    {
        Program.Require(!_pending && !_unknown, "Slot prepared while submission acceptance/completion unknown.");
        if (_spec.Cold)
        {
            _pipeline.Dispose();
            _pipeline = _device.CreateGraphicsPipeline(new()
            {
                VertexShader = _vertex,
                FragmentShader = _fragment,
            });
        }

        if (_spec.Replace)
        {
            await CreateMeshesAsync(frame);
        }
    }

    internal void Encode(int frame)
    {
        Program.Require(_commands.Count == 0 && !_pending && !_unknown, "Slot commands not retired.");
        DrawCalls = 0;
        CommandCalls = 0;
        SubmitCalls = 0;
        for (int part = 0; part < _spec.Commands; part++)
        {
            IGraphicsCommandBuffer c = _device.CreateCommandBuffer(new());
            _commands.Add(c);
            CommandCalls++;
            if (part == 0)
            {
                Program.Transition(c, _color, _initialized ? TextureState.ColorAttachment : TextureState.Undefined, TextureState.ColorAttachment, _initialized ? new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) : default, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
                if (_depth is not null)
                {
                    Program.Transition(c, _depth, _initialized ? TextureState.DepthStencilAttachment : TextureState.Undefined, TextureState.DepthStencilAttachment, _initialized ? new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite) : default, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite));
                }

                if (!_initialized && _white is not null)
                {
                    Program.Transition(c, _white, TextureState.Undefined, TextureState.ColorAttachment, default, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
                    IRenderEncoder clear = c.BeginRenderPass(new()
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
                    Program.Transition(c, _white, TextureState.ColorAttachment, TextureState.Sampled, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
                }

                if (_needsUpload)
                {
                    foreach (ModelMesh mesh in _meshes)
                    {
                        mesh.RecordInitialUpload(c);
                    }
                }
            }
            else
            {
                Program.Transition(c, _color, TextureState.ColorAttachment, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
            }

            IRenderEncoder render = c.BeginRenderPass(new()
            {
                ColorAttachments =
                [
                    new()
                    {
                        View = _colorView,
                        LoadOp = part == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                    },
                ],
                DepthStencilAttachment = _depthView is null ? null : new()
                {
                    View = _depthView,
                },
            });
            render.SetPipeline(_pipeline);
            render.SetDynamicState(new(0, 0, Program.Size, Program.Size, 0, 1), new(0, 0, Program.Size, Program.Size));
            for (int draw = 0; draw < _spec.Draws / _spec.Commands; draw++)
            {
                if (_spec.Model)
                {
                    int index = _spec.Unique ? draw : 0;
                    ModelMaterial material = ModelDrawing.DefaultMaterial with
                    {
                        BaseColor = new(1, 1, 1, _spec.Transparent ? 0.5f : 1),
                        Unlit = 1,
                        AlphaMode = _spec.Transparent ? 2u : 0u,
                    };
                    ModelDrawArguments arguments = ModelDrawing.CreateArguments(_vertexRefs[index], material, _sampled, _sampled) with
                    {
                        World = Matrix4x4.CreateTranslation(0, 0, -draw * 0.01f),
                    };
                    render.SetRenderState(ModelDrawing.CreateRenderState(material, doubleSided: true));
                    render.SetArgumentTable(_table!);
                    render.SetArguments(in arguments);
                    _meshes[index].Draw(render);
                    DrawCalls++;
                }
                else
                {
                    bool second = _spec.States == "alternating" ? draw % 2 == 1 : _spec.States == "grouped" && draw >= _spec.Draws / 2;

                    // Two real PSO raster variants with identical pixels: triangle is double-sided.
                    render.SetRenderState(new()
                    {
                        Rasterization = new()
                        {
                            Cull = CullMode.None,
                            FrontFace = second ? FrontFace.Clockwise : FrontFace.CounterClockwise,
                        },
                        ColorTargets = [new()],
                    });
                    render.Draw(3);
                    DrawCalls++;
                }
            }

            render.End();
            c.Finish();
        }

        _initialized = true;
    }

    internal void Submit()
    {
        _submitStart = Stopwatch.GetTimestamp();
        _pending = true;
        try
        {
            if (_spec.Submits == 1)
            {
                _submissions.Add(_device.Queue.Submit(_commands));
                SubmitCalls++;
            }
            else
            {
                foreach (IGraphicsCommandBuffer c in _commands)
                {
                    _submissions.Add(_device.Queue.Submit([c]));
                    SubmitCalls++;
                }
            }
        }
        catch
        {
            _unknown = true;
            throw;
        }
    }

    internal async Task CompleteAsync()
    {
        if (!_pending)
        {
            return;
        }

        try
        {
            foreach (IGraphicsSubmission submission in _submissions)
            {
                await submission.WaitAsync();
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

        if (Frame >= 0)
        {
            CompletionSamples[Frame] = Program.Ms(_submitStart);
        }

        Frame = -1;
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

    internal async Task<byte[]> ReadPixelsAsync()
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
            await submission.WaitAsync();
            _pending = false;
        }
        catch
        {
            _unknown = true;
            throw;
        }

        await _readback.MapAsync();
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
        }

        _meshes.Clear();
        int count = _spec.Unique ? _spec.Draws : 1;
        Vector4 vertexColor = _spec.Replace && frame % 2 == 1 ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
        var vertices = new ModelVertex[_spec.Triangles * 3];
        uint[] indices = new uint[vertices.Length];
        for (int triangle = 0; triangle < _spec.Triangles; triangle++)
        {
            // Partition one full-screen triangle; more vertices do not increase coverage or alpha overdraw.
            Vector3 a = new(-1, -1, 0.5f);
            Vector3 b = new(3, -1, 0.5f);
            Vector3 c = new(-1, 3, 0.5f);
            Vector3[] positions = [a, Vector3.Lerp(b, c, (float)triangle / _spec.Triangles), Vector3.Lerp(b, c, (float)(triangle + 1) / _spec.Triangles)];
            for (int j = 0; j < 3; j++)
            {
                int i = (triangle * 3) + j;
                vertices[i] = new(positions[j], Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, vertexColor);
                indices[i] = (uint)i;
            }
        }

        for (int i = 0; i < count; i++)
        {
            _meshes.Add(await ModelMesh.CreateAsync(_device, vertices, indices, _vertexArtifact));
        }

        _table = _device.CreateArgumentTable(new()
        {
            BufferCapacity = (uint)count,
            TextureCapacity = 1,
            SamplerCapacity = 1,
        });
        _vertexRefs = new IGpuRef<ModelVertex>[count];
        for (int i = 0; i < count; i++)
        {
            _vertexRefs[i] = _table.WriteBuffer((uint)i, _meshes[i].Vertices.SliceElements(0, _meshes[i].Vertices.Count));
        }

        _sampled = new(_table.WriteTexture(0, _whiteView!), _table.WriteSampler(0, _sampler!));
        _needsUpload = true;
    }
}
