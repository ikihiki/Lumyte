using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class PreparedGraphGpuBatch2D : IPreparedGraphCommandBatch2D
{
    private static readonly BarrierScope _fragmentRead = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private readonly GraphPreparationContext2D _context;
    private readonly List<IDisposable> _owned = [];
    private readonly List<PreparedCommand> _commands = [];
    private readonly GraphicsProgramLease _program;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly IGraphicsTexture _fallback;
    private readonly IGraphicsTextureView _fallbackView;
    private readonly ShaderArtifact _artifact;
    private bool _appended;
    private bool _disposed;

    private PreparedGraphGpuBatch2D(GraphPreparationContext2D context, CancellationToken cancellationToken)
    {
        _context = context;
        try
        {
            GraphicsProgramLease program = context.Programs.Acquire(context.Device, DrawingPrograms2D.ProgramId);
            ArgumentNullException.ThrowIfNull(program);
            _program = Own(program);
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsProgramResources resources = program.Resources;
            if (!ReferenceEquals(resources.Device, context.Device))
            {
                throw new ArgumentException("The program belongs to a different physical device.");
            }

            _artifact = resources.FragmentArtifact;
            _pipeline = resources.Pipeline;
            _sampler = resources.DefaultSampler ?? Own(context.Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 }));
            _fallback = Own(context.Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled }));
            _fallbackView = Own(_fallback.CreateView());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _program.Resources;
        if (_appended || !ReferenceEquals(graph, _context.Graph) || target != _context.Target)
        {
            throw new InvalidOperationException("A prepared batch can only be appended once to its original graph and target.");
        }

        _appended = true;
        graph.Retain(new ProgramValidation(_program));
        RenderGraphTexture fallback = graph.ImportTexture(_fallback, TextureState.Undefined, default, "unused 2D texture binding");
        graph.AddPass("Initialize unused 2D texture binding", context =>
        {
            IRenderEncoder clear = context.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _fallbackView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
            clear.End();
        }).UseTexture(fallback, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        foreach (PreparedCommand command in _commands)
        {
            AppendCommand(graph, target, command, fallback);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }
    }

    internal static async ValueTask<IPreparedGraphCommandBatch2D> PrepareGraphAsync(GraphPreparationContext2D context, GraphImageSource2D source, DrawingState2D state, float opacity, GraphImageSource2D? original, bool replace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batch = new PreparedGraphGpuBatch2D(context, cancellationToken);
        try
        {
            await batch.AddAsync(null, state, source, original, opacity, replace, cancellationToken).ConfigureAwait(false);
            return batch;
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    private static Vector4 Bounds(GraphImageSource2D? image) => image is null ? default : new(image.Bounds.X, image.Bounds.Y, image.Bounds.Width, image.Bounds.Height);

    private async ValueTask AddAsync(DrawingCommand2D? command, DrawingState2D? clip, GraphImageSource2D? source, GraphImageSource2D? original, float opacity, bool replace, CancellationToken cancellationToken)
    {
        IArgumentTable table = Own(_context.Device.CreateArgumentTable(new() { TextureCapacity = DrawingProgram2D.MaximumInstructions + 3, SamplerCapacity = DrawingProgram2D.MaximumInstructions + 2, BufferCapacity = 2 }));
        IGpuRef<IGraphicsSampler> sampler = table.WriteSampler(0, _sampler);
        IGpuRef<IGraphicsTextureView> fallbackRef = table.WriteTexture(DrawingProgram2D.MaximumInstructions + 2, _fallbackView);
        var program = new DrawingProgram2D(table, sampler, _context.Target);
        uint root = command is not null ? program.Compile(command) : program.CompileClip(clip!);
        program.ValidateFiniteData();
        for (int i = 0; i < program.Instructions.Count; i++)
        {
            DrawingInstruction instruction = program.Instructions[i];
            program.Instructions[i] = instruction with { Texture = instruction.Texture ?? fallbackRef, Sampler = instruction.Sampler ?? sampler };
        }

        if (program.Data.Count == 0)
        {
            program.Data.Add(default);
        }

        foreach (IGraphicsTexture texture in program.Textures)
        {
            _context.Bindings.GetTexture(texture);
        }

        IGraphicsShaderDataBuffer<DrawingInstruction> instructions = Own(_context.Device.CreateBuffer<DrawingInstruction>(_artifact, (ulong)program.Instructions.Count));
        IGraphicsShaderDataBuffer<DrawingInstruction> upload = Own(_context.Device.CreateBuffer<DrawingInstruction>(_artifact, instructions.Count, MemoryPreference.Upload));
        IGraphicsBuffer<Vector4> data = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = (ulong)program.Data.Count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        await upload.MapAsync().ConfigureAwait(false);
        try
        {
            upload.CopyFrom(program.Instructions.ToArray());
        }
        finally
        {
            upload.Unmap();
        }

        await data.MapAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            data.CopyFrom(program.Data.ToArray());
        }
        finally
        {
            data.Unmap();
        }

        cancellationToken.ThrowIfCancellationRequested();
        IGpuRef<DrawingInstruction> instructionRef = table.WriteBuffer(0, instructions.SliceElements(0, instructions.Count));
        ScissorRect scissor = DrawingBounds2D.GetScissor(command, clip, _context.Target, source is null ? null : new Rect2D(source.Bounds.X, source.Bounds.Y, source.Bounds.Width, source.Bounds.Height), original is not null, replace);
        _commands.Add(new(program, table, instructions, upload, data, instructionRef, sampler, fallbackRef, root, source, original, opacity, replace, scissor));
    }

    private void AppendCommand(RenderGraphBuilder graph, RenderTargetBinding2D target, PreparedCommand command, RenderGraphTexture fallback)
    {
        RenderGraphShaderDataBuffer<DrawingInstruction> instructions = graph.ImportShaderDataBuffer(command.Instructions, default, "drawing instructions");
        RenderGraphShaderDataBuffer<DrawingInstruction> upload = graph.ImportShaderDataBuffer(command.Upload, new(PipelineStage.Host, ResourceAccess.HostWrite), "drawing instruction upload");
        RenderGraphBuffer<Vector4> dataUpload = graph.ImportBuffer(command.Data, new(PipelineStage.Host, ResourceAccess.HostWrite), "drawing geometry upload");
        RenderGraphBuffer<Vector4> data = graph.CreateBuffer<Vector4>(new() { Count = command.Data.Count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }, "drawing geometry");
        graph.AddPass("Upload 2D command", context =>
        {
            context.Commands.CopyBuffer(command.Upload.SliceElements(0, command.Upload.Count), command.Instructions.SliceElements(0, command.Instructions.Count));
            context.Commands.CopyBuffer(command.Data.Slice(0, command.Data.Count), context.GetBuffer(data).Slice(0, command.Data.Count));
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

            IGraphicsTextureView view = context.CreateTextureView(target.Texture);
            IGpuRef<Vector4> dataRef = command.Table.WriteBuffer(1, context.GetBuffer(data).Slice(0, command.Data.Count));
            IGpuRef<IGraphicsTextureView> sourceRef = command.Source is null ? command.Fallback : command.Table.WriteTexture(DrawingProgram2D.MaximumInstructions, context.CreateTextureView(command.Source.Texture));
            IGpuRef<IGraphicsTextureView> originalRef = command.Original is null ? command.Fallback : command.Table.WriteTexture(DrawingProgram2D.MaximumInstructions + 1, context.CreateTextureView(command.Original.Texture));
            var arguments = new DrawingArguments(command.InstructionRef, dataRef, (uint)command.Program.Instructions.Count, command.Root, command.Source is null ? 0u : command.Original is null ? 1u : 2u, command.Opacity, Bounds(command.Source), Bounds(command.Original), new(target.PixelOrigin, 0, 0), sourceRef, originalRef, command.Sampler);
            IRenderEncoder render = context.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            render.SetPipeline(_pipeline);
            render.SetArgumentTable(command.Table);
            render.SetRenderState(new()
            {
                Rasterization = new() { Cull = CullMode.None },
                ColorTargets = [new() { BlendEnable = !command.Replace && command.Original is null, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
            });
            render.SetViewport(new(0, 0, target.Description.Width, target.Description.Height, 0, 1));
            render.SetScissor(command.Scissor);
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            render.SetArguments(in arguments);
            render.Draw(3);
            render.End();
        })
        .UseTexture(target.Texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite))
        .UseShaderDataBuffer(instructions, _fragmentRead)
        .UseBuffer(data, _fragmentRead);

        HashSet<RenderGraphTexture> sampled = [fallback];
        foreach (IGraphicsTexture texture in command.Program.Textures)
        {
            sampled.Add(_context.Bindings.GetTexture(texture));
        }

        if (command.Source is not null)
        {
            sampled.Add(command.Source.Texture);
        }

        if (command.Original is not null)
        {
            sampled.Add(command.Original.Texture);
        }

        foreach (RenderGraphTexture texture in sampled)
        {
            if (texture == target.Texture)
            {
                throw new ArgumentException("A command cannot sample its active drawing target.");
            }

            pass.UseTexture(texture, TextureState.Sampled, _fragmentRead);
        }
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private sealed record PreparedCommand(DrawingProgram2D Program, IArgumentTable Table, IGraphicsShaderDataBuffer<DrawingInstruction> Instructions, IGraphicsShaderDataBuffer<DrawingInstruction> Upload, IGraphicsBuffer<Vector4> Data, IGpuRef<DrawingInstruction> InstructionRef, IGpuRef<IGraphicsSampler> Sampler, IGpuRef<IGraphicsTextureView> Fallback, uint Root, GraphImageSource2D? Source, GraphImageSource2D? Original, float Opacity, bool Replace, ScissorRect Scissor);

    private sealed class ProgramValidation(GraphicsProgramLease program) : IRenderGraphLifetime
    {
        public void Validate() => _ = program.Resources;

        public void Dispose()
        {
        }
    }
}
