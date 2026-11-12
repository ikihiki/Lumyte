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
    private readonly ShaderArtifact _artifact;
    private IGraphicsShaderDataBuffer<DrawingInstruction>? _instructions;
    private IGraphicsShaderDataBuffer<DrawingInstruction>? _upload;
    private IGraphicsBuffer<Vector4>? _data;
    private IGraphicsBuffer<Vector4>? _geometry;
    private long _estimatedBytes = 4096;
    private bool _recorded;
    private bool _disposed;

    private PreparedGpuBatch2D(RenderPreparationContext2D context, CancellationToken cancellationToken)
    {
        _context = context;
        try
        {
            Own(context.RetainResources());
            Own(context.ArgumentPlan.Retain());
            cancellationToken.ThrowIfCancellationRequested();
            DrawingResources2D resources = context.Resources;
            _artifact = resources.Pipeline.Desc.FragmentShader?.Artifact
                ?? throw new ArgumentException("Drawing requires a fragment shader.");
            _pipeline = resources.Pipeline;
            _sampler = context.ArgumentPlan.Sampler(resources);
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(this, error);
            throw;
        }
    }

    // Includes staging/device allocations, packed CPU payloads and conservative descriptor overhead.
    public long EstimatedBytes => checked(_estimatedBytes + _context.ArgumentPlan.EstimatedBytes);

    /// <inheritdoc/>
    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IGraphicsTextureView view in _commands.SelectMany(command => command.Program.Views).Distinct(ReferenceEqualityComparer.Instance).Cast<IGraphicsTextureView>())
        {
            resources.UseTextureView(view, _fragmentRead);
        }

        resources.UseSampler(_sampler);
        foreach (IGraphicsSampler sampler in _commands.SelectMany(command => command.Program.Samplers).Distinct(ReferenceEqualityComparer.Instance).Cast<IGraphicsSampler>())
        {
            resources.UseSampler(sampler);
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
        if (_upload?.IsMapped == true)
        {
            _upload.Unmap();
        }

        if (_data?.IsMapped == true)
        {
            _data.Unmap();
        }

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

        (int Scopes, int Uploads) checkpoint = context.ArgumentPlan.Checkpoint();
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
                context.ArgumentPlan.Rollback(checkpoint);
                return await PreparedIndividualGpuBatch2D.PrepareAsync(context, commands, cancellationToken).ConfigureAwait(false);
            }

            batch.AllocateStorage();
            if (batch._commands.Count != 0)
            {
                context.ArgumentPlan.DeferUpload(batch._commands[0].Reservation, batch.UploadAsync, batch.MapStorageAsync, batch.UploadMapped);
            }

            return batch;
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(batch, error);
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
        _context.ArgumentPlan.InitializeFallback(commands);

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

        foreach (PreparedCommand command in _commands)
        {
            // Opaque draws resolve the existing registrations and validate sampled
            // resource lifetimes on every record, including resident draws.
            RecordCommand(commands, target, command);
        }
    }

    private void Add(DrawingCommand2D command)
    {
        DrawingArgumentPlan2D plan = _context.ArgumentPlan;
        ShaderResources reservation = plan.Reserve();
        var program = new DrawingProgram2D(reservation, _context.Target);
        uint root = program.Compile(command);
        program.ValidateFiniteData();
        if (program.Data.Count == 0)
        {
            program.Data.Add(default);
        }

        ScissorRect scissor = DrawingBounds2D.GetScissor(command, null, _context.Target, null, false, false);
        var preparedCommand = new PreparedCommand(program, reservation, root, scissor);
        reservation.RegisterSampler(_sampler, reference => preparedCommand.ResolvedSampler = reference);
        plan.Fallback(reservation, reference => preparedCommand.ResolvedFallback = reference);
        _commands.Add(preparedCommand);
        _estimatedBytes = checked(_estimatedBytes + ((long)program.Instructions.Count * 256) + ((long)program.Data.Count * 32) + 65536);
    }

    private void AllocateStorage()
    {
        if (_commands.Count == 0)
        {
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

        ulong instructionOffset = 0;
        ulong dataOffset = 0;
        foreach (PreparedCommand command in _commands)
        {
            command.Reservation.RegisterBuffer(_instructions.SliceElements(instructionOffset, (ulong)command.Program.Instructions.Count), reference => command.ResolvedInstructions = reference);
            command.Reservation.RegisterBuffer(_geometry.SliceElements(dataOffset, (ulong)command.Program.Data.Count), reference => command.ResolvedData = reference);
            BarrierScope prepared = new(PipelineStage.Copy | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead);
            _context.ArgumentPlan.MarkPreparedShaderData(command.Reservation, _instructions.SliceElements(instructionOffset, (ulong)command.Program.Instructions.Count), prepared);
            _context.ArgumentPlan.MarkPreparedBuffer(command.Reservation, _geometry.SliceElements(dataOffset, (ulong)command.Program.Data.Count), prepared);
            instructionOffset += (ulong)command.Program.Instructions.Count;
            dataOffset += (ulong)command.Program.Data.Count;
        }

        _estimatedBytes = checked(_estimatedBytes + (long)(_instructions.SizeInBytes + _upload.SizeInBytes + _data.SizeInBytes + _geometry.SizeInBytes));
    }

    private async ValueTask MapStorageAsync(CancellationToken cancellationToken)
    {
        if (_commands.Count == 0)
        {
            return;
        }

        try
        {
            await _upload!.MapAsync(cancellationToken).ConfigureAwait(false);
            await _data!.MapAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_upload!.IsMapped)
            {
                _upload.Unmap();
            }

            if (_data!.IsMapped)
            {
                _data.Unmap();
            }

            throw;
        }
    }

    private async ValueTask UploadAsync(CancellationToken cancellationToken)
    {
        await MapStorageAsync(cancellationToken).ConfigureAwait(false);
        UploadMapped();
    }

    private void UploadMapped()
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

            if (!_upload!.IsMapped || !_data!.IsMapped)
            {
                throw new InvalidOperationException("Prepare staging maps before synchronous payload finalization.");
            }

            ulong instructionOffset = 0;
            ulong dataOffset = 0;
            foreach (PreparedCommand command in _commands)
            {
                // One opaque batch belongs to one graph pass, so all its command scopes are live or culled together.
                if (command.Reservation.IsReady)
                {
                    command.Program.ResolveReferences(command.Fallback, command.Sampler);
                    _upload.CopyFrom(CollectionsMarshal.AsSpan(command.Program.Instructions), instructionOffset);
                    copied += (long)((ulong)command.Program.Instructions.Count * _upload.ShaderElementStrideInBytes);
                    _data.SliceElements(dataOffset, (ulong)command.Program.Data.Count).CopyFrom(CollectionsMarshal.AsSpan(command.Program.Data));
                    copied += (long)command.Program.Data.Count * 16;
                }

                instructionOffset += (ulong)command.Program.Instructions.Count;
                dataOffset += (ulong)command.Program.Data.Count;
            }

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (_upload?.IsMapped == true)
            {
                _upload.Unmap();
            }

            if (_data?.IsMapped == true)
            {
                _data.Unmap();
            }

            profile?.Counter(8, copied);
        }
    }

    private void RecordCommand(IGraphicsCommandBuffer commands, IGraphicsTextureView view, PreparedCommand command)
    {
        if (!command.Reservation.IsReady)
        {
            throw new InvalidOperationException("Materialize the resource set before recording.");
        }

        if (command.Scissor.Width == 0 || command.Scissor.Height == 0)
        {
            return;
        }

        DrawingTargetInfo2D target = _context.Target;
        var arguments = new DrawingArguments(command.InstructionRef, command.DataRef, (uint)command.Program.Instructions.Count, command.Root, 0, 1, default, default, new(target.PixelOrigin, 0, 0), command.Fallback, command.Fallback, command.Sampler);
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(_pipeline);
        render.SetRenderState(new()
        {
            Rasterization = new() { Cull = CullMode.None },
            ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
        });
        render.SetViewport(new(0, 0, target.Description.Width, target.Description.Height, 0, 1));
        render.SetScissor(command.Scissor);
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        command.Reservation.Draw(render, in arguments, 3);
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

    private sealed record PreparedCommand(DrawingProgram2D Program, ShaderResources Reservation, uint Root, ScissorRect Scissor)
    {
        internal IGpuRef<DrawingInstruction>? ResolvedInstructions { get; set; }

        internal IGpuRef<Vector4>? ResolvedData { get; set; }

        internal IGpuRef<IGraphicsSampler>? ResolvedSampler { get; set; }

        internal IGpuRef<IGraphicsTextureView>? ResolvedFallback { get; set; }

        internal IGpuRef<DrawingInstruction> InstructionRef => Ready(ResolvedInstructions);

        internal IGpuRef<Vector4> DataRef => Ready(ResolvedData);

        internal IGpuRef<IGraphicsSampler> Sampler => Ready(ResolvedSampler);

        internal IGpuRef<IGraphicsTextureView> Fallback => Ready(ResolvedFallback);

        private IGpuRef<T> Ready<T>(IGpuRef<T>? reference) => Reservation.IsReady && reference is not null
            ? reference
            : throw new InvalidOperationException("Materialize the resource set before using native references.");
    }
}
