using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A scene-wide GPU pipeline; it never dispatches the standard raster batch.</summary>
internal sealed class ComputeExecution2D : IRetainedCommandBatch2D, IRetainedMemory2D
{
    private static readonly BarrierScope _computeRead = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
    private static readonly BarrierScope _computeWrite = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite);
    private static readonly string[] _stageNames = ["initialize", "curves", "stroke", "tile-count", "tile-scan", "tile-fill", "backdrop", "draw-count", "draw-scan", "draw-fill", "shade"];
    private readonly RenderPreparationContext2D _context;
    private readonly List<IDisposable> _owned = [];
    private readonly List<IUpload> _uploads = [];
    private readonly List<DrawingPass2D> _passes = [];
    private readonly ShaderResources _computeScope;
    private readonly ShaderResources _resolveScope;
    private ComputeExecutionPlan2D _cpu = null!;
    private IGraphicsComputePipeline _compute = null!;
    private IGraphicsPipeline _resolve = null!;
    private RawUpload<Vector4> _input = null!;
    private ShaderUpload<DrawRecord2D> _records = null!;
    private ShaderUpload<DrawDirectoryEntry2D> _paints = null!;
    private IGpuRef<DrawDirectoryEntry2D> _paintRef = null!;
    private uint _clipOffset;
    private ShaderUpload<MaskDescriptor2D> _masks = null!;
    private ShaderUpload<ImagePaintData2D> _images = null!;
    private IGraphicsBuffer<Vector4> _storage = null!;
    private IGraphicsBuffer<uint> _work = null!;
    private IGpuRef<Vector4> _storageRef = null!;
    private IGpuRef<Vector4> _colorRef = null!;
    private IGpuRef<uint> _workRef = null!;
    private IGpuRef<IGraphicsSampler> _sampler = null!;
    private bool _recorded;
    private bool _disposed;

    private ComputeExecution2D(RenderPreparationContext2D context)
    {
        _context = context;
        try
        {
            Own(context.RetainResources());
            Own(context.ArgumentPlan.Retain());
            _computeScope = context.ArgumentPlan.Reserve();
            _resolveScope = context.ArgumentPlan.Reserve();
            if (!context.ArgumentPlan.DeferredMaterialization)
            {
                // This private DrawLocal owner guarantees distinct RO/RW tables;
                // execution kind alone does not prevent whole-table projection.
                context.ArgumentPlan.Registrations.ConfigureExecution(_computeScope, 0, false);
                context.ArgumentPlan.Registrations.ConfigureExecution(_resolveScope, 1, true);
            }
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(this, error);
            throw;
        }
    }

    /// <summary>A mapped upload stream and its destination-resource declarations.</summary>
    private interface IUpload
    {
        /// <summary>Maps the staging resource for CPU writes.</summary>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The mapping operation.</returns>
        ValueTask MapAsync(CancellationToken token);

        /// <summary>Unmaps the staging resource if it is mapped.</summary>
        void Unmap();

        /// <summary>Copies staging contents to the GPU destination.</summary>
        /// <param name="commands">The command buffer.</param>
        void Copy(IGraphicsCommandBuffer commands);

        /// <summary>Declares the staging read and destination write.</summary>
        /// <param name="resources">The pass resource declarations.</param>
        void DeclareCopy(IDrawingResourceDeclarations2D resources);

        /// <summary>Declares the compute-stage destination read.</summary>
        /// <param name="resources">The pass resource declarations.</param>
        void DeclareRead(IDrawingResourceDeclarations2D resources);
    }

    public long EstimatedBytes => checked(_cpu.EstimatedBytes + _context.ArgumentPlan.EstimatedBytes);

    internal IReadOnlyList<DrawingPass2D> Passes => _passes;

    internal IGraphicsBuffer<uint> DiagnosticStatus => _work;

    public void AccountMemory(Dictionary<object, long> owners)
    {
        owners.TryAdd(this, _cpu.EstimatedBytes);
        owners.TryAdd(_context.ArgumentPlan, _context.ArgumentPlan.EstimatedBytes);
    }

    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        foreach (IGraphicsTextureView view in _cpu.Views)
        {
            resources.UseTextureView(view, _computeRead);
        }

        foreach (IGraphicsSampler sampler in _cpu.Samplers)
        {
            resources.UseSampler(sampler);
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => RecordRetained(commands, target, false);

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded && !resident)
        {
            throw new InvalidOperationException("A compute scene can be recorded only once.");
        }

        if (_cpu.Views.Any(view => ReferenceEquals(view.Texture, target.Texture)))
        {
            throw new ArgumentException("A compute scene cannot sample its destination attachment.", nameof(target));
        }

        _recorded = true;
        _context.ValidateResources();
        _context.ArgumentPlan.InitializeFallback(commands);
        if (!resident)
        {
            Copy(commands);
            for (uint stage = 0; stage < _stageNames.Length; stage++)
            {
                Dispatch(commands, stage);
            }
        }
        else
        {
            // Texture contents remain borrowed and may change between completed frames.
            // Reuse immutable geometry/binning only, never a stale painted image.
            commands.Barrier(new BufferBarrierDesc<Vector4>
            {
                Buffer = _storage.SliceElements(0, _storage.Count),
                Before = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead),
                After = _computeWrite,
            });
            Dispatch(commands, 10);
        }

        Resolve(commands, target);
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (IUpload upload in _uploads)
        {
            upload.Unmap();
        }

        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawingCommand2D> commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        context.ValidateResources();
        var arguments = new DrawingArgumentPlan2D(context.Device) { ArgumentTableMode = DrawingArgumentTableMode.DrawLocal };
        ComputeExecution2D batch;
        try
        {
            batch = new ComputeExecution2D(context.WithArgumentPlan(arguments));
        }
        catch
        {
            arguments.Dispose();
            throw;
        }

        batch.Own(arguments);
        batch.Own(context.ArgumentPlan.Retain());
        context = batch._context;
        try
        {
            batch._cpu = ComputeExecutionPlan2D.Encode(context, commands, batch._computeScope, cancellationToken);
            batch.Allocate();
            context.ArgumentPlan.DeferUpload(batch._computeScope, batch.UploadAsync, batch.MapAsync, batch.Fill);
            batch._passes.Add(new("drawing2d.tiled.upload", (commands, _) => batch.Copy(commands), batch.DeclareCopies, [], UsesTarget: false, LocalResources: true));
            for (uint stage = 0; stage < _stageNames.Length; stage++)
            {
                uint selected = stage;
                batch._passes.Add(new("drawing2d.tiled." + _stageNames[stage], (commands, _) => batch.Dispatch(commands, selected), resources => batch.DeclareCompute(resources, selected), [], UsesTarget: false, LocalResources: true));
            }

            batch._passes.Add(new("drawing2d.tiled.resolve", batch.Resolve, batch.DeclareResolve, [], LocalResources: true));
            await arguments.SealAsync(cancellationToken).ConfigureAwait(false);
            if (batch.EstimatedBytes > context.MaxWorkingBytes)
            {
                throw new NotSupportedException("Tiled compute materialized tables exceed the working-byte budget.");
            }

            return batch;
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(batch, error);
            throw;
        }
    }

    private void Allocate()
    {
        IGraphicDevice device = _context.Device;
        IGraphicsShader shader = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(ComputeExecution2D).Assembly, "Lumyte.Drawing2D.tiled-scene-compute.lshader")));
        _compute = Own(device.CreateComputePipeline(new() { ComputeShader = shader }));
        IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(ComputeExecution2D).Assembly, "Lumyte.Drawing2D.tiled-resolve-vertex.lshader")));
        IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(ComputeExecution2D).Assembly, "Lumyte.Drawing2D.tiled-resolve-fragment.lshader")));
        _resolve = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        ShaderArtifact artifact = shader.Artifact;
        _records = Shader<DrawRecord2D>(Math.Max(1, _cpu.Page.Records.Count), 96, artifact);
        _paints = Shader<DrawDirectoryEntry2D>(_cpu.Page.Paints.Count + _cpu.Page.Clips.Count, 16, artifact, false);
        _computeScope.RegisterBuffer(_paints.Data.SliceElements(0, _paints.Data.Count), reference => _paintRef = reference);
        _clipOffset = (uint)_cpu.Page.Paints.Count;
        _masks = Shader<MaskDescriptor2D>(Math.Max(1, _cpu.Page.Masks.Count), 80, artifact);
        _images = Shader<ImagePaintData2D>(Math.Max(1, _cpu.Page.Images.Count), 128, artifact);
        _input = Raw<Vector4>(_cpu.Input.Count + Math.Max(1, _cpu.Page.Numeric.Count));
        if (device.GetBufferLayout<Vector4>().ElementStrideInBytes != 16 || device.GetBufferLayout<uint>().ElementStrideInBytes != 4)
        {
            throw new NotSupportedException("Tiled compute requires canonical float4 and uint storage strides.");
        }

        _storage = Own(device.CreateBuffer<Vector4>(new() { Count = _cpu.StorageCount, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite }));
        _work = Own(device.CreateBuffer<uint>(new() { Count = _cpu.WorkCount, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite }));
        _computeScope.RegisterBuffer(_storage.SliceElements(0, _storage.Count), reference => _storageRef = reference, ArgumentBufferAccess.ReadWrite);
        _computeScope.RegisterBuffer(_work.SliceElements(0, _work.Count), reference => _workRef = reference, ArgumentBufferAccess.ReadWrite);

        // Resolve sees only the output range and cannot expose an RW view to
        // graphics. Compute and resolve reservations must not be joined.
        _resolveScope.RegisterBuffer(_storage.SliceElements(_cpu.ColorOffset, _cpu.PixelCount), reference => _colorRef = reference);
        IGraphicsSampler sampler = _context.ArgumentPlan.Sampler(_context.Resources);
        _cpu.Samplers.Add(sampler);
        _computeScope.RegisterSampler(sampler, reference => _sampler = reference);
    }

    private ShaderUpload<T> Shader<T>(int count, ulong stride, ShaderArtifact artifact, bool register = true)
        where T : struct, IShaderArguments
    {
        var stream = new ShaderUpload<T>(this, count, stride, artifact, register);
        _uploads.Add(stream);
        return stream;
    }

    private RawUpload<T> Raw<T>(int count)
        where T : unmanaged
    {
        var stream = new RawUpload<T>(this, count);
        _uploads.Add(stream);
        return stream;
    }

    private async ValueTask MapAsync(CancellationToken token)
    {
        foreach (IUpload upload in _uploads)
        {
            await upload.MapAsync(token).ConfigureAwait(false);
        }
    }

    private async ValueTask UploadAsync(CancellationToken token)
    {
        await MapAsync(token).ConfigureAwait(false);
        Fill();
    }

    private void Fill()
    {
        try
        {
            _cpu.Page.Resolve(null!, _sampler);
            _records.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Records));
            _paints.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Paints));
            _paints.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Clips), (ulong)_cpu.Page.Paints.Count);
            _masks.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Masks));
            _images.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Images));
            _input.Fill(CollectionsMarshal.AsSpan(_cpu.Input));
            _input.Fill(CollectionsMarshal.AsSpan(_cpu.Page.Numeric), (ulong)_cpu.Input.Count);
        }
        finally
        {
            foreach (IUpload upload in _uploads)
            {
                upload.Unmap();
            }
        }
    }

    private void Copy(IGraphicsCommandBuffer commands)
    {
        foreach (IUpload upload in _uploads)
        {
            upload.Copy(commands);
        }
    }

    private void DeclareCopies(IDrawingResourceDeclarations2D resources)
    {
        foreach (IUpload upload in _uploads)
        {
            upload.DeclareCopy(resources);
        }
    }

    private void DeclareCompute(IDrawingResourceDeclarations2D resources, uint stage)
    {
        foreach (IUpload upload in _uploads)
        {
            upload.DeclareRead(resources);
        }

        BarrierScope scope = stage == 0 ? new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite) : _computeWrite;
        resources.UseBuffer(_storage, scope);
        resources.UseBuffer(_work, scope);
        DeclareResources(resources);
    }

    private void DeclareResolve(IDrawingResourceDeclarations2D resources)
        => resources.UseBuffer(_storage.SliceElements(_cpu.ColorOffset, _cpu.PixelCount), new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));

    private void Dispatch(IGraphicsCommandBuffer commands, uint stage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var args = new ComputeExecutionArguments2D
        {
            Records = _records.Reference,
            PaintDirectory = _paintRef,
            MaskDescriptors = _masks.Reference,
            ImagePaintData = _images.Reference,
            Input = _input.Reference,
            Storage = _storageRef,
            Work = _workRef,
            Stage = stage,
            ClipOffset = _clipOffset,
            NumericOffset = (uint)_cpu.Input.Count,
        };
        uint[] c = _cpu.Configuration;
        long invocations = stage switch
        {
            0 => Math.Max(_cpu.StorageCount, _cpu.WorkCount),
            1 => c[1],
            2 => c[0],
            3 or 5 => c[4],
            6 => _cpu.BackdropRows,
            7 or 9 => c[5],
            10 => _cpu.PixelCount,
            _ => 1,
        };

        // Every global producer/consumer boundary is a real dispatch and memory
        // barrier, not an invalid workgroup-only synchronization primitive.
        if (stage > 0)
        {
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _storage.SliceElements(0, _storage.Count), Before = _computeWrite, After = _computeWrite });
            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _work.SliceElements(0, _work.Count), Before = _computeWrite, After = _computeWrite });
        }

        IComputeEncoder encoder = commands.BeginComputePass(new());
        encoder.SetPipeline(_compute);
        _computeScope.Dispatch(encoder, in args, ComputeExecutionPlan2D.Groups(invocations, _context.Device.Caps), 1);
        encoder.End();
    }

    private void Resolve(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cpu.Views.Any(view => ReferenceEquals(view.Texture, target.Texture)))
        {
            throw new ArgumentException("A compute scene cannot sample its destination attachment.", nameof(target));
        }

        DrawingTargetInfo2D info = _context.Target;
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _storage.SliceElements(_cpu.ColorOffset, _cpu.PixelCount), Before = _computeWrite, After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(_resolve);
        render.SetRenderState(new()
        {
            Rasterization = new() { Cull = CullMode.None },
            ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
        });
        render.SetViewport(new(0, 0, info.Description.Width, info.Description.Height, 0, 1));
        render.SetScissor(new(0, 0, info.Description.Width, info.Description.Height));
        render.SetBlendConstant(default);
        render.SetStencilReference(0);
        var args = new ComputeExecutionResolveArguments2D { Color = _colorRef, Width = info.Description.Width, Height = info.Description.Height };
        _resolveScope.Draw(render, in args, 3, 1, 0, 0);
        render.End();
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
    }

    private T Own<T>(T item)
        where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }

    private sealed class ShaderUpload<T> : IUpload
        where T : struct, IShaderArguments
    {
        private readonly IGraphicsShaderDataBuffer<T> _upload;
        private readonly IGraphicsShaderDataBuffer<T> _data;

        internal ShaderUpload(ComputeExecution2D owner, int count, ulong stride, ShaderArtifact artifact, bool register)
        {
            _upload = owner.Own(owner._context.Device.CreateShaderDataBuffer<T>(artifact, (ulong)count, MemoryPreference.Upload));
            _data = owner.Own(owner._context.Device.CreateShaderDataBuffer<T>(artifact, (ulong)count));
            if (_upload.ShaderElementStrideInBytes != stride || _data.ShaderElementStrideInBytes != stride)
            {
                throw new NotSupportedException("Tiled compute shader data ABI stride mismatch.");
            }

            if (register)
            {
                owner._computeScope.RegisterBuffer(_data.SliceElements(0, _data.Count), reference => Reference = reference);
            }
        }

        internal IGraphicsShaderDataBuffer<T> Data => _data;

        internal IGpuRef<T> Reference { get; private set; } = null!;

        public ValueTask MapAsync(CancellationToken token) => _upload.MapAsync(token);

        public void Unmap()
        {
            if (_upload.IsMapped)
            {
                _upload.Unmap();
            }
        }

        public void Copy(IGraphicsCommandBuffer commands)
        {
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = _upload.SliceElements(0, _upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(_upload.SliceElements(0, _upload.Count), _data.SliceElements(0, _data.Count));
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = _data.SliceElements(0, _data.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = _computeRead });
        }

        public void DeclareCopy(IDrawingResourceDeclarations2D resources)
        {
            resources.UseShaderDataBuffer(_upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            resources.UseShaderDataBuffer(_data, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        }

        public void DeclareRead(IDrawingResourceDeclarations2D resources) => resources.UseShaderDataBuffer(_data, _computeRead);

        internal void Fill(ReadOnlySpan<T> values, ulong offset = 0)
        {
            if (values.IsEmpty)
            {
                return; // Unused typed descriptors must not capture null texture references.
            }
            else
            {
                _upload.CopyFrom(values, offset);
            }
        }
    }

    private sealed class RawUpload<T> : IUpload
        where T : unmanaged
    {
        private readonly IGraphicsBuffer<T> _upload;
        private readonly IGraphicsBuffer<T> _data;

        internal RawUpload(ComputeExecution2D owner, int count)
        {
            _upload = owner.Own(owner._context.Device.CreateBuffer<T>(new() { Count = (ulong)count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
            _data = owner.Own(owner._context.Device.CreateBuffer<T>(new() { Count = (ulong)count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
            owner._computeScope.RegisterBuffer(_data.SliceElements(0, _data.Count), reference => Reference = reference);
        }

        internal IGpuRef<T> Reference { get; private set; } = null!;

        public ValueTask MapAsync(CancellationToken token) => _upload.MapAsync(token);

        public void Unmap()
        {
            if (_upload.IsMapped)
            {
                _upload.Unmap();
            }
        }

        public void Copy(IGraphicsCommandBuffer commands)
        {
            commands.Barrier(new BufferBarrierDesc<T> { Buffer = _upload.SliceElements(0, _upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(_upload.SliceElements(0, _upload.Count), _data.SliceElements(0, _data.Count));
            commands.Barrier(new BufferBarrierDesc<T> { Buffer = _data.SliceElements(0, _data.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = _computeRead });
        }

        public void DeclareCopy(IDrawingResourceDeclarations2D resources)
        {
            resources.UseBuffer(_upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            resources.UseBuffer(_data, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        }

        public void DeclareRead(IDrawingResourceDeclarations2D resources) => resources.UseBuffer(_data, _computeRead);

        internal void Fill(ReadOnlySpan<T> values, ulong offset = 0)
        {
            if (values.IsEmpty)
            {
                T value = default;
                _upload.CopyBytesFrom(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)), offset * (ulong)Marshal.SizeOf<T>(), (ulong)Marshal.SizeOf<T>());
            }
            else
            {
                _upload.CopyBytesFrom(MemoryMarshal.AsBytes(values), offset * (ulong)Marshal.SizeOf<T>(), (ulong)MemoryMarshal.AsBytes(values).Length);
            }
        }
    }
}
