using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// Deliberately retains the original per-command allocation route for portable targets
// and oversized batches. Compiler/evaluator fixes must remain common to both routes.
internal sealed class PreparedIndividualGpuBatch2D : IRetainedCommandBatch2D
{
    private static readonly BarrierScope _fragmentRead = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private readonly RenderPreparationContext2D _context;
    private readonly List<IDisposable> _owned = [];
    private readonly List<PreparedCommand> _commands = [];
    private readonly List<Action> _mappedUploads = [];
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly ShaderArtifact _artifact;
    private long _profileInstructions;
    private long _profileVectors;
    private long _profileCopied;
    private long _estimatedBytes = 4096;
    private bool _recorded;
    private bool _disposed;

    private PreparedIndividualGpuBatch2D(RenderPreparationContext2D context, CancellationToken cancellationToken)
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
        UnmapStaging();
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands, CancellationToken cancellationToken)
        where T : DrawingCommand2D
    {
        using var profile = DrawingProfileScope.Start(8);
        PreparedIndividualGpuBatch2D? batch = null;
        try
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            batch = new PreparedIndividualGpuBatch2D(context, cancellationToken);
            try
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await batch.AddAsync(commands.Span[i], cancellationToken, profile is not null).ConfigureAwait(false);
                }

                if (batch._commands.Count != 0)
                {
                    context.ArgumentPlan.DeferUpload(batch._commands[0].Reservation, batch.UploadAsync, batch.MapStorageAsync, batch.UploadMapped);
                }

                profile?.Success();
                return batch;
            }
            catch (Exception error)
            {
                context.CleanupAfterFailure(batch, error);
                throw;
            }
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (batch is not null)
            {
                profile?.Counter(6, batch._profileInstructions);
                profile?.Counter(7, batch._profileVectors);
                profile?.Counter(8, batch._profileCopied);
            }
        }
    }

    private void RecordCore(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        _context.ArgumentPlan.InitializeFallback(commands);

        foreach (PreparedCommand command in _commands)
        {
            // Opaque draws resolve the existing registrations and validate sampled
            // resource lifetimes on every record, including resident draws.
            RecordCommand(commands, target, command, resident);
        }
    }

    private ValueTask AddAsync(DrawingCommand2D command, CancellationToken cancellationToken, bool profiling)
    {
        if (_context.Clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements).Kind == NormalizedClipKind2D.Empty)
        {
            return ValueTask.CompletedTask;
        }

        DrawingArgumentPlan2D plan = _context.ArgumentPlan;
        ShaderResources reservation = plan.Reserve();
        var program = new DrawingProgram2D(reservation, _context.Target, _context.Clips);
        uint root = program.Compile(command);
        program.ValidateFiniteData();
        if (program.Data.Count == 0)
        {
            program.Data.Add(default);
        }

        if (profiling)
        {
            _profileInstructions += program.Instructions.Count;
            _profileVectors += program.Data.Count;
        }

        IGraphicsShaderDataBuffer<DrawingInstruction> instructions = Own(_context.Device.CreateShaderDataBuffer<DrawingInstruction>(_artifact, (ulong)program.Instructions.Count));
        IGraphicsShaderDataBuffer<DrawingInstruction> upload = Own(_context.Device.CreateShaderDataBuffer<DrawingInstruction>(_artifact, instructions.Count, MemoryPreference.Upload));
        IGraphicsBuffer<Vector4> data = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = (ulong)program.Data.Count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        IGraphicsBuffer<Vector4> geometry = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = data.Count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        BarrierScope prepared = new(PipelineStage.Copy | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead);
        plan.MarkPreparedShaderData(reservation, instructions.SliceElements(0, instructions.Count), prepared);
        plan.MarkPreparedBuffer(reservation, geometry.SliceElements(0, geometry.Count), prepared);
        ScissorRect scissor = DrawingBounds2D.GetScissor(command, null, _context.Target, null, false, false, program.Clip?.Bounds);
        var preparedCommand = new PreparedCommand(program, reservation, instructions, upload, data, geometry, root, scissor);
        reservation.RegisterSampler(_sampler, reference => preparedCommand.ResolvedSampler = reference);
        plan.Fallback(reservation, reference => preparedCommand.ResolvedFallback = reference);
        reservation.RegisterBuffer(instructions.SliceElements(0, instructions.Count), reference => preparedCommand.ResolvedInstructions = reference);
        reservation.RegisterBuffer(geometry.SliceElements(0, geometry.Count), reference => preparedCommand.ResolvedData = reference);
        _mappedUploads.Add(() =>
        {
            if (!reservation.IsReady)
            {
                return;
            }

            program.ResolveReferences(preparedCommand.Fallback, preparedCommand.Sampler);
            if (!upload.IsMapped || !data.IsMapped)
            {
                throw new InvalidOperationException("Prepare staging maps before synchronous graph finalization.");
            }

            try
            {
                upload.CopyFrom(program.Instructions.ToArray());
                if (profiling)
                {
                    _profileCopied += (long)((ulong)program.Instructions.Count * upload.ShaderElementStrideInBytes);
                }

                data.CopyFrom(program.Data.ToArray());
                if (profiling)
                {
                    _profileCopied += (long)program.Data.Count * 16;
                }
            }
            finally
            {
                upload.Unmap();
                data.Unmap();
            }
        });
        _estimatedBytes = checked(_estimatedBytes + (long)(instructions.SizeInBytes + upload.SizeInBytes + data.SizeInBytes + geometry.SizeInBytes)
            + ((long)program.Instructions.Count * 256) + ((long)program.Data.Count * 32) + 65536);
        _commands.Add(preparedCommand);
        return ValueTask.CompletedTask;
    }

    private async ValueTask MapStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (PreparedCommand command in _commands)
            {
                await command.Upload.MapAsync(cancellationToken).ConfigureAwait(false);
                await command.Data.MapAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            UnmapStaging();
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
        using var profile = DrawingProfileScope.Start(8);
        try
        {
            foreach (Action upload in _mappedUploads)
            {
                upload();
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
            UnmapStaging();
            profile?.Counter(8, _profileCopied);
            _mappedUploads.Clear();
        }
    }

    private void UnmapStaging()
    {
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
    }

    private void RecordCommand(IGraphicsCommandBuffer commands, IGraphicsTextureView view, PreparedCommand command, bool resident)
    {
        if (!command.Reservation.IsReady)
        {
            throw new InvalidOperationException("Materialize the resource set before recording.");
        }

        if (!resident)
        {
            var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
            commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = command.Upload.SliceElements(0, command.Upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = command.Data.SliceElements(0, command.Data.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
            commands.CopyBuffer(command.Upload.SliceElements(0, command.Upload.Count), command.Instructions.SliceElements(0, command.Instructions.Count));
            commands.CopyBuffer(command.Data.SliceElements(0, command.Data.Count), command.Geometry.SliceElements(0, command.Geometry.Count));
            commands.Barrier(new ShaderDataBufferBarrierDesc<DrawingInstruction> { Buffer = command.Instructions.SliceElements(0, command.Instructions.Count), Before = copyWrite, After = _fragmentRead });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = command.Geometry.SliceElements(0, command.Geometry.Count), Before = copyWrite, After = _fragmentRead });
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

    private sealed record PreparedCommand(DrawingProgram2D Program, ShaderResources Reservation, IGraphicsShaderDataBuffer<DrawingInstruction> Instructions, IGraphicsShaderDataBuffer<DrawingInstruction> Upload, IGraphicsBuffer<Vector4> Data, IGraphicsBuffer<Vector4> Geometry, uint Root, ScissorRect Scissor)
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
