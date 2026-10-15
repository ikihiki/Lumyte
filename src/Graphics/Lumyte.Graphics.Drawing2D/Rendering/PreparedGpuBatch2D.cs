using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedGpuBatch2D : IPreparedCommandBatch2D
{
    private static readonly BarrierScope _fragmentRead = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private readonly RenderPreparationContext2D _context;
    private readonly List<IDisposable> _owned = [];
    private readonly List<PreparedCommand> _commands = [];
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly IGraphicsTexture _fallback;
    private readonly IGraphicsTextureView _fallbackView;
    private readonly ShaderArtifact _artifact;
    private bool _recorded;
    private bool _disposed;

    private PreparedGpuBatch2D(RenderPreparationContext2D context, CancellationToken cancellationToken)
    {
        _context = context;
        try
        {
            GraphicsProgramLease program = context.Programs.Acquire(context.Device, DrawingPrograms2D.ProgramId);
            ArgumentNullException.ThrowIfNull(program);
            Own(program);
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsProgramResources resources = program.Resources;
            if (!ReferenceEquals(resources.Device, context.Device))
            {
                throw new ArgumentException("The program belongs to a different physical device.");
            }

            _artifact = resources.FragmentArtifact;
            _pipeline = resources.Pipeline;
            _sampler = Own(context.Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 }));
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
    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IGraphicsTexture texture in _commands.SelectMany(command => command.Program.Textures).Distinct(ReferenceEqualityComparer.Instance).Cast<IGraphicsTexture>())
        {
            resources.UseTexture(texture, TextureState.Sampled, _fragmentRead);
        }
    }

    /// <inheritdoc/>
    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded)
        {
            throw new InvalidOperationException("A prepared command batch can be recorded only once.");
        }

        if (_commands.Any(command => command.Program.Textures.Any(texture => ReferenceEquals(texture, target.Texture))))
        {
            throw new ArgumentException("A command cannot sample its active drawing target.", nameof(target));
        }

        _recorded = true;
        var attachmentWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = attachmentWrite });
        IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _fallbackView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        clear.End();
        commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = attachmentWrite, After = _fragmentRead });
        foreach (PreparedCommand command in _commands)
        {
            RecordCommand(commands, target, command);
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

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands, CancellationToken cancellationToken)
        where T : DrawingCommand2D
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var batch = new PreparedGpuBatch2D(context, cancellationToken);
        try
        {
            for (int i = 0; i < commands.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await batch.AddAsync(commands.Span[i], cancellationToken).ConfigureAwait(false);
            }

            return batch;
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    private async ValueTask AddAsync(DrawingCommand2D command, CancellationToken cancellationToken)
    {
        IArgumentTable table = Own(_context.Device.CreateArgumentTable(new() { TextureCapacity = DrawingProgram2D.MaximumInstructions + 3, SamplerCapacity = DrawingProgram2D.MaximumInstructions + 2, BufferCapacity = 2 }));
        IGpuRef<IGraphicsSampler> sampler = table.WriteSampler(0, _sampler);
        IGpuRef<IGraphicsTextureView> fallbackRef = table.WriteTexture(DrawingProgram2D.MaximumInstructions + 2, _fallbackView);
        var program = new DrawingProgram2D(table, sampler, _context.Target);
        uint root = program.Compile(command);
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
        ScissorRect scissor = DrawingBounds2D.GetScissor(command, null, _context.Target, null, false, false);
        IGraphicsBuffer<Vector4> geometry = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = data.Count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        _commands.Add(new(program, table, instructions, upload, data, geometry, instructionRef, sampler, fallbackRef, root, scissor));
    }

    private void RecordCommand(IGraphicsCommandBuffer commands, IGraphicsTextureView view, PreparedCommand command)
    {
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = command.Upload.SliceElements(0, command.Upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = command.Data.Slice(0, command.Data.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
        commands.CopyBuffer(command.Upload.SliceElements(0, command.Upload.Count), command.Instructions.SliceElements(0, command.Instructions.Count));
        commands.CopyBuffer(command.Data.Slice(0, command.Data.Count), command.Geometry.Slice(0, command.Geometry.Count));
        commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = command.Instructions.SliceElements(0, command.Instructions.Count), Before = copyWrite, After = _fragmentRead });
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = command.Geometry.Slice(0, command.Geometry.Count), Before = copyWrite, After = _fragmentRead });
        if (command.Scissor.Width == 0 || command.Scissor.Height == 0)
        {
            return;
        }

        DrawingTargetInfo2D target = _context.Target;
        IGpuRef<Vector4> dataRef = command.Table.WriteBuffer(1, command.Geometry.Slice(0, command.Geometry.Count));
        var arguments = new DrawingArguments(command.InstructionRef, dataRef, (uint)command.Program.Instructions.Count, command.Root, 0, 1, default, default, new(target.PixelOrigin, 0, 0), command.Fallback, command.Fallback, command.Sampler);
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(_pipeline);
        render.SetArgumentTable(command.Table);
        render.SetRenderState(new()
        {
            Rasterization = new() { Cull = CullMode.None },
            ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
        });
        render.SetViewport(new(0, 0, target.Description.Width, target.Description.Height, 0, 1));
        render.SetScissor(command.Scissor);
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        render.SetArguments(in arguments);
        render.Draw(3);
        render.End();
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = view.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private sealed record PreparedCommand(DrawingProgram2D Program, IArgumentTable Table, IGraphicsShaderDataBuffer<DrawingInstruction> Instructions, IGraphicsShaderDataBuffer<DrawingInstruction> Upload, IGraphicsBuffer<Vector4> Data, IGraphicsBuffer<Vector4> Geometry, IGpuRef<DrawingInstruction> InstructionRef, IGpuRef<IGraphicsSampler> Sampler, IGpuRef<IGraphicsTextureView> Fallback, uint Root, ScissorRect Scissor);
}
