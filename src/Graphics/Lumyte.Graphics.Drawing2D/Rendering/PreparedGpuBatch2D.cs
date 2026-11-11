using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedGpuBatch2D : IRetainedCommandBatch2D
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
    private IGraphicsShaderDataBuffer<DrawingInstruction>? _instructions;
    private IGraphicsShaderDataBuffer<DrawingInstruction>? _upload;
    private IGraphicsBuffer<Vector4>? _data;
    private IGraphicsBuffer<Vector4>? _geometry;
    private bool _recorded;
    private bool _disposed;

    private PreparedGpuBatch2D(RenderPreparationContext2D context, CancellationToken cancellationToken)
    {
        _context = context;
        try
        {
            Own(context.RetainResources());
            cancellationToken.ThrowIfCancellationRequested();
            DrawingResources2D resources = context.Resources;
            _artifact = resources.Pipeline.Desc.FragmentShader?.Artifact
                ?? throw new ArgumentException("Drawing requires a fragment shader.");
            _pipeline = resources.Pipeline;
            _sampler = resources.Sampler ?? Own(context.Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 }));
            _fallback = Own(context.Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled }));
            _fallbackView = Own(_fallback.CreateView());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // Includes staging/device allocations, packed CPU payloads and conservative descriptor overhead.
    public long EstimatedBytes { get; private set; } = 4096;

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
        _context.ValidateResources();
        if (_recorded)
        {
            throw new InvalidOperationException("A prepared command batch can be recorded only once.");
        }

        if (_commands.Any(command => command.Program.Textures.Any(texture => ReferenceEquals(texture, target.Texture))))
        {
            throw new ArgumentException("A command cannot sample its active drawing target.", nameof(target));
        }

        _recorded = true;
        RecordCore(commands, target, resident: false);
    }

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ValidateResources();
        if (_commands.Any(command => command.Program.Textures.Any(texture => ReferenceEquals(texture, target.Texture))))
        {
            throw new ArgumentException("A command cannot sample its active drawing target.", nameof(target));
        }

        RecordCore(commands, target, resident);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed && _owned.Count == 0)
        {
            return;
        }

        _disposed = true;
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands, CancellationToken cancellationToken)
        where T : DrawingCommand2D
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // SPIR-V uses native addresses; portable descriptor offsets require alignment.
        if (context.Device.Caps.ShaderTarget != ShaderTarget.SpirV || commands.Length < 2)
        {
            return await PreparedIndividualGpuBatch2D.PrepareAsync(context, commands, cancellationToken).ConfigureAwait(false);
        }

        var batch = new PreparedGpuBatch2D(context, cancellationToken);
        try
        {
            using (var compile = DrawingProfileScope.Start(6))
            {
                try
                {
                    for (int i = 0; i < commands.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        batch.Add(commands.Span[i]);
                    }

                    compile?.Success();
                }
                catch (Exception error)
                {
                    compile?.Failure(error);
                    throw;
                }
                finally
                {
                    if (compile is { CountersEnabled: true })
                    {
                        compile.Counter(6, batch._commands.Sum(item => (long)item.Program.Instructions.Count));
                        compile.Counter(7, batch._commands.Sum(item => (long)item.Program.Data.Count));
                    }
                }
            }

            ulong instructionCount = checked((ulong)batch._commands.Sum(item => (long)item.Program.Instructions.Count));
            ulong dataCount = checked((ulong)batch._commands.Sum(item => (long)item.Program.Data.Count));
            bool fits;
            using (IGraphicsShaderDataBuffer<DrawingInstruction> probe = context.Device.CreateShaderDataBuffer<DrawingInstruction>(batch._artifact, 1))
            {
                fits = FitsSharedStorage(instructionCount, dataCount, probe.ShaderElementStrideInBytes, context.Device.Caps.MaxBufferSize);
            }

            if (!fits)
            {
                batch.Dispose();
                return await PreparedIndividualGpuBatch2D.PrepareAsync(context, commands, cancellationToken).ConfigureAwait(false);
            }

            await batch.UploadAsync(cancellationToken).ConfigureAwait(false);
            return batch;
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    internal static bool FitsSharedStorage(ulong instructionCount, ulong dataCount, ulong instructionStride, ulong maximumBytes)
    {
        // Vulkan mapped uploads address each program with a checked signed byte offset.
        // Bound both combined allocations conservatively before choosing the shared route.
        ulong uploadBytes = Math.Min(maximumBytes, (ulong)int.MaxValue);
        return instructionStride > 0 && instructionCount <= int.MaxValue
            && instructionCount <= uploadBytes / instructionStride && dataCount <= uploadBytes / 16;
    }

    private void RecordCore(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        if (!resident)
        {
            var attachmentWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = attachmentWrite });
            IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _fallbackView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
            clear.End();
            commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = attachmentWrite, After = _fragmentRead });
        }

        if (!resident && _instructions is not null)
        {
            var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
            commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = _upload!.SliceElements(0, _upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _data!.SliceElements(0, _data.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
            commands.CopyBuffer(_upload.SliceElements(0, _upload.Count), _instructions.SliceElements(0, _instructions.Count));
            commands.CopyBuffer(_data.SliceElements(0, _data.Count), _geometry!.SliceElements(0, _geometry.Count));
            commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = _instructions.SliceElements(0, _instructions.Count), Before = copyWrite, After = _fragmentRead });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _geometry.SliceElements(0, _geometry.Count), Before = copyWrite, After = _fragmentRead });
        }

        IRenderEncoder? render = null;
        foreach (PreparedCommand command in _commands)
        {
            if (command.Scissor.Width == 0 || command.Scissor.Height == 0)
            {
                continue;
            }

            // Ordered attachment blending preserves painter order inside one pass.
            // Sampled-target feedback is rejected before entering RecordCore.
            render ??= BeginBatchPass(commands, target);
            RecordCommand(render, command);
        }

        if (render is not null)
        {
            render.End();
            var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        }
    }

    private void Add(DrawingCommand2D command)
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

        ScissorRect scissor = DrawingBounds2D.GetScissor(command, null, _context.Target, null, false, false);
        _commands.Add(new(program, table, sampler, fallbackRef, root, scissor));
        EstimatedBytes = checked(EstimatedBytes + ((long)program.Instructions.Count * 256) + ((long)program.Data.Count * 32) + 65536);
    }

    private async ValueTask UploadAsync(CancellationToken cancellationToken)
    {
        using var profile = DrawingProfileScope.Start(7);
        long copied = 0;
        try
        {
            if (_commands.Count == 0)
            {
                profile?.Success();
                return;
            }

            // Keep each program's indices local by binding ranges of the shared allocation.
            ulong instructionCount = 0;
            ulong dataCount = 0;
            foreach (PreparedCommand command in _commands)
            {
                instructionCount = checked(instructionCount + (ulong)command.Program.Instructions.Count);
                dataCount = checked(dataCount + (ulong)command.Program.Data.Count);
            }

            _instructions = Own(_context.Device.CreateShaderDataBuffer<DrawingInstruction>(_artifact, instructionCount));
            _upload = Own(_context.Device.CreateShaderDataBuffer<DrawingInstruction>(_artifact, instructionCount, MemoryPreference.Upload));
            _data = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = dataCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
            _geometry = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = dataCount, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
            await _upload.MapAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ulong offset = 0;
                foreach (PreparedCommand command in _commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _upload.CopyFrom(CollectionsMarshal.AsSpan(command.Program.Instructions), offset);
                    if (profile is not null)
                    {
                        copied += (long)((ulong)command.Program.Instructions.Count * _upload.ShaderElementStrideInBytes);
                    }

                    offset += (ulong)command.Program.Instructions.Count;
                }
            }
            finally
            {
                _upload.Unmap();
            }

            await _data.MapAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ulong offset = 0;
                foreach (PreparedCommand command in _commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _data.SliceElements(offset, (ulong)command.Program.Data.Count).CopyFrom(CollectionsMarshal.AsSpan(command.Program.Data));
                    copied += (long)command.Program.Data.Count * 16;
                    offset += (ulong)command.Program.Data.Count;
                }
            }
            finally
            {
                _data.Unmap();
            }

            cancellationToken.ThrowIfCancellationRequested();
            ulong instructionOffset = 0;
            ulong dataOffset = 0;
            foreach (PreparedCommand command in _commands)
            {
                command.InstructionRef = command.Table.WriteBuffer(0, _instructions.SliceElements(instructionOffset, (ulong)command.Program.Instructions.Count));
                command.DataRef = command.Table.WriteBuffer(1, _geometry.SliceElements(dataOffset, (ulong)command.Program.Data.Count));
                instructionOffset += (ulong)command.Program.Instructions.Count;
                dataOffset += (ulong)command.Program.Data.Count;
            }

            EstimatedBytes = checked(EstimatedBytes + (long)(_instructions.SizeInBytes + _upload.SizeInBytes + _data.SizeInBytes + _geometry.SizeInBytes));

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            profile?.Counter(8, copied);
        }
    }

    private IRenderEncoder BeginBatchPass(IGraphicsCommandBuffer commands, IGraphicsTextureView view)
    {
        DrawingTargetInfo2D target = _context.Target;
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(_pipeline);
        render.SetRenderState(new()
        {
            Rasterization = new() { Cull = CullMode.None },
            ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
        });
        render.SetViewport(new(0, 0, target.Description.Width, target.Description.Height, 0, 1));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        return render;
    }

    private void RecordCommand(IRenderEncoder render, PreparedCommand command)
    {
        DrawingTargetInfo2D target = _context.Target;
        var arguments = new DrawingArguments(command.InstructionRef, command.DataRef, (uint)command.Program.Instructions.Count, command.Root, 0, 1, default, default, new(target.PixelOrigin, 0, 0), command.Fallback, command.Fallback, command.Sampler);

        // Resolve and validate every command's sampled resources, including resident draws.
        render.SetArgumentTable(command.Table);
        render.SetScissor(command.Scissor);
        render.SetArguments(in arguments);
        render.Draw(3);
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private sealed record PreparedCommand(DrawingProgram2D Program, IArgumentTable Table, IGpuRef<IGraphicsSampler> Sampler, IGpuRef<IGraphicsTextureView> Fallback, uint Root, ScissorRect Scissor)
    {
        internal IGpuRef<DrawingInstruction> InstructionRef { get; set; } = null!;

        internal IGpuRef<Vector4> DataRef { get; set; } = null!;
    }
}
