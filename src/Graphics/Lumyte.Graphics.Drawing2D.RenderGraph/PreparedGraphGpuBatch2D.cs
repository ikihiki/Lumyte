using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class PreparedGraphGpuBatch2D : IPreparedGraphCommandBatch2D
{
    private static readonly BarrierScope _fragmentRead = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private readonly GraphPreparationContext2D _context;
    private readonly GraphDrawingArguments2D _shared;
    private readonly List<IDisposable> _owned = [];
    private readonly List<PreparedCommand> _commands = [];
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly ShaderArtifact _artifact;
    private bool _appended;
    private bool _disposed;

    private PreparedGraphGpuBatch2D(GraphPreparationContext2D context, CancellationToken cancellationToken)
    {
        _context = context;
        _shared = GraphDrawingArguments2D.Get(context.Graph);
        try
        {
            Own(context.ResourceState.Retain());
            cancellationToken.ThrowIfCancellationRequested();
            DrawingResources2D resources = context.Resources;
            _artifact = resources.Pipeline.Desc.FragmentShader?.Artifact ?? throw new ArgumentException("Drawing requires a fragment shader.");
            _pipeline = resources.Pipeline;
            _sampler = resources.Sampler ?? context.Graph.GetPreparedSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
        }
        catch (Exception error)
        {
            context.ResourceState.CleanupAfterFailure(this, error);
            throw;
        }
    }

    public void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ResourceState.Validate();
        if (_appended || !ReferenceEquals(graph, _context.Graph) || target != _context.Target)
        {
            throw new InvalidOperationException("Append a prepared batch once to its original graph and target.");
        }

        _appended = true;
        graph.Retain(new ResourceValidation(_context.ResourceState));
        foreach (PreparedCommand command in _commands)
        {
            AppendCommand(graph, target, command);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (PreparedCommand command in _commands)
        {
            if (command.Upload.IsMapped)
            {
                command.Upload.Unmap();
            }

            if (command.Data.IsMapped)
            {
                command.Data.Unmap();
            }
        }

        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedGraphCommandBatch2D> PrepareGraphAsync(GraphPreparationContext2D context, GraphImageSource2D source, DrawingState2D state, float opacity, GraphImageSource2D? original, bool replace, CancellationToken cancellationToken)
    {
        var batch = new PreparedGraphGpuBatch2D(context, cancellationToken);
        try
        {
            await batch.AddAsync(state, source, original, opacity, replace, cancellationToken).ConfigureAwait(false);
            return batch;
        }
        catch (Exception error)
        {
            context.ResourceState.CleanupAfterFailure(batch, error);
            throw;
        }
    }

    private static Vector4 Bounds(GraphImageSource2D? image) => image is null ? default : new(image.Bounds.X, image.Bounds.Y, image.Bounds.Width, image.Bounds.Height);

    private async ValueTask AddAsync(DrawingState2D clip, GraphImageSource2D source, GraphImageSource2D? original, float opacity, bool replace, CancellationToken cancellationToken)
    {
        ShaderResources resources = _context.Graph.CreatePreparedPayload();
        var program = new DrawingProgram2D(resources, _context.Target);
        uint root = program.CompileClip(clip);
        program.ValidateFiniteData();
        if (program.Data.Count == 0)
        {
            program.Data.Add(default);
        }

        IGraphicsShaderDataBuffer<DrawingInstruction> upload = Own(_context.Device.CreateShaderDataBuffer<DrawingInstruction>(_artifact, (ulong)program.Instructions.Count, MemoryPreference.Upload));
        IGraphicsBuffer<Vector4> data = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = (ulong)program.Data.Count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        ScissorRect scissor = DrawingBounds2D.GetScissor(null, clip, _context.Target, new(source.Bounds.X, source.Bounds.Y, source.Bounds.Width, source.Bounds.Height), original is not null, replace);
        var command = new PreparedCommand(program, resources, upload, data, root, source, original, opacity, replace, scissor);
        command.RegisterReferences(_context.Graph, _sampler, _shared.Fallback);
        _commands.Add(command);
        await upload.MapAsync(cancellationToken).ConfigureAwait(false);
        await data.MapAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AppendCommand(RenderGraphBuilder graph, RenderTargetBinding2D target, PreparedCommand command)
    {
        RenderGraphShaderDataBuffer<DrawingInstruction> instructions = graph.CreateShaderDataBuffer<DrawingInstruction>(_artifact, (ulong)command.Program.Instructions.Count, "drawing instructions");
        RenderGraphShaderDataBuffer<DrawingInstruction> upload = graph.ImportShaderDataBuffer(command.Upload, new(PipelineStage.Host, ResourceAccess.HostWrite), "drawing instruction upload");
        RenderGraphBuffer<Vector4> dataUpload = graph.ImportBuffer(command.Data, new(PipelineStage.Host, ResourceAccess.HostWrite), "drawing geometry upload");
        RenderGraphBuffer<Vector4> data = graph.CreateBuffer<Vector4>(new() { Count = command.Data.Count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }, "drawing geometry");
        graph.AddPass("Upload 2D command", context =>
        {
            context.Commands.CopyBuffer(command.Upload.SliceElements(0, command.Upload.Count), context.GetShaderDataBuffer(instructions).SliceElements(0, command.Upload.Count));
            context.Commands.CopyBuffer(command.Data.SliceElements(0, command.Data.Count), context.GetBuffer(data).SliceElements(0, command.Data.Count));
        })
        .UseShaderDataBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead))
        .UseShaderDataBuffer(instructions, new(PipelineStage.Copy, ResourceAccess.CopyWrite))
        .UseBuffer(dataUpload, new(PipelineStage.Copy, ResourceAccess.CopyRead))
        .UseBuffer(data, new(PipelineStage.Copy, ResourceAccess.CopyWrite));

        RenderGraphPass pass = graph.AddPass("Draw 2D command", context =>
        {
            if (command.Scissor.Width == 0 || command.Scissor.Height == 0)
            {
                return;
            }

            using RenderGraphRenderEncoder render = context.BeginRenderPass(new RenderGraphRenderPassDesc { ColorAttachments = [new() { Texture = target.Texture, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            render.SetPipeline(_pipeline);
            render.SetRenderState(new()
            {
                Rasterization = new() { Cull = CullMode.None },
                ColorTargets = [new() { BlendEnable = !command.Replace && command.Original is null, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
            });
            render.SetViewport(new(0, 0, target.Description.Width, target.Description.Height, 0, 1));
            render.SetScissor(command.Scissor);
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            var arguments = new DrawingArguments(
                context.GetGpuRef(instructions, 0, command.Upload.Count),
                context.GetGpuRef(data, 0, command.Data.Count),
                (uint)command.Program.Instructions.Count,
                command.Root,
                command.Original is null ? 1u : 2u,
                command.Opacity,
                Bounds(command.Source),
                Bounds(command.Original),
                new Vector4(target.PixelOrigin, 0, 0),
                context.GetGpuRef(command.Source.Texture),
                context.GetGpuRef(command.Original?.Texture ?? _shared.Fallback),
                context.GetGpuRef(_sampler));
            render.SetArguments(in arguments);
            render.Draw(3);
        })
        .UseTexture(target.Texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite))
        .UseShaderDataBuffer(instructions, 0, command.Upload.Count, _fragmentRead)
        .UseBuffer(data, 0, command.Data.Count, _fragmentRead);

        HashSet<RenderGraphTexture> sampled = [_shared.Fallback, command.Source.Texture];
        if (command.Original is not null)
        {
            sampled.Add(command.Original.Texture);
        }

        foreach (RenderGraphTexture texture in sampled)
        {
            if (ReferenceEquals(texture, target.Texture))
            {
                throw new ArgumentException("A command cannot sample its active target.");
            }

            pass.UseTexture(texture, TextureState.Sampled, _fragmentRead);
        }

        foreach (IGraphicsTextureView view in command.Program.Views)
        {
            pass.UseNativeView(_context.Bindings.GetTexture(view.Texture), view, _fragmentRead);
        }

        foreach (IGraphicsSampler sampler in command.Program.Samplers)
        {
            pass.UseSampler(sampler);
        }

        pass.UseSampler(_sampler);
        pass.AttachPreparedPayload(command.Resources);
        command.Resources.WhenReady(() =>
        {
            if (!command.Upload.IsMapped || !command.Data.IsMapped)
            {
                throw new InvalidOperationException("Map composite staging during graph append.");
            }

            try
            {
                command.ResolveReferences();
                command.Upload.CopyFrom(command.Program.Instructions.ToArray());
                command.Data.CopyFrom(command.Program.Data.ToArray());
            }
            finally
            {
                command.Upload.Unmap();
                command.Data.Unmap();
            }
        });
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private sealed class PreparedCommand(DrawingProgram2D program, ShaderResources resources, IGraphicsShaderDataBuffer<DrawingInstruction> upload, IGraphicsBuffer<Vector4> data, uint root, GraphImageSource2D source, GraphImageSource2D? original, float opacity, bool replace, ScissorRect scissor)
    {
        private IGpuRef<IGraphicsSampler>? _sampler;
        private IGpuRef<IGraphicsTextureView>? _fallback;

        internal DrawingProgram2D Program { get; } = program;

        internal ShaderResources Resources { get; } = resources;

        internal IGraphicsShaderDataBuffer<DrawingInstruction> Upload { get; } = upload;

        internal IGraphicsBuffer<Vector4> Data { get; } = data;

        internal uint Root { get; } = root;

        internal GraphImageSource2D Source { get; } = source;

        internal GraphImageSource2D? Original { get; } = original;

        internal float Opacity { get; } = opacity;

        internal bool Replace { get; } = replace;

        internal ScissorRect Scissor { get; } = scissor;

        internal void RegisterReferences(RenderGraphBuilder graph, IGraphicsSampler sampler, RenderGraphTexture fallback)
        {
            Resources.RegisterSampler(sampler, value => _sampler = value);
            graph.ConnectPreparedTexture(Resources, fallback, value => _fallback = value);
        }

        internal void ResolveReferences()
        {
            if (!Resources.IsReady)
            {
                throw new InvalidOperationException("Materialize the command resources before resolving instructions.");
            }

            Program.ResolveReferences(
                _fallback ?? throw new InvalidOperationException("The fallback texture reference is unresolved."),
                _sampler ?? throw new InvalidOperationException("The sampler reference is unresolved."));
        }
    }

    private sealed class ResourceValidation(DrawingResourceState2D resources) : IRenderGraphLifetime
    {
        public void Validate() => resources.Validate();

        public void Dispose()
        {
        }
    }
}
