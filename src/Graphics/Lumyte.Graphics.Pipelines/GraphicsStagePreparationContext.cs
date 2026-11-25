using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Prepares device storage and record-only callbacks within declared finite limits.</summary>
public sealed class GraphicsStagePreparationContext
{
    private readonly GraphicsRun _run;
    private readonly StagePreparationContext _scope;
    private readonly int _node;
    private readonly GraphicsStageRequirements? _requirements;
    private readonly GraphicsStageBuildContext _build;
    private long _outputBytes;
    private bool _preflight;
    private long _scratchCredit;
    private long _transferCredit;

    internal GraphicsStagePreparationContext(GraphicsRun run, StagePreparationContext scope, int node, GraphicsStageRequirements? requirements, GraphicsStageBuildContext build)
    {
        _run = run;
        _scope = scope;
        _node = node;
        _requirements = requirements;
        _build = build;
    }

    internal GraphicsRun Run => _run;

    internal StagePreparationContext Scope => _scope;

    internal int Node => _node;

    /// <summary>Reserves a canonical device output within this stage's declared limit.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <param name="extent">The finite canonical extent.</param>
    /// <returns>The invocation-owned output lease.</returns>
    public ValueLease<Device<TSchema>> AllocateDeviceOutput<TSchema>(SchemaExtent extent)
        where TSchema : ISchema
    {
        ArgumentNullException.ThrowIfNull(extent);
        if (_requirements is null || _requirements.Domain is GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download)
        {
            throw new InvalidOperationException("Device output allocation requires a declared GPU stage.");
        }

        bool dependency = TSchema.Descriptor.Metadata.TryGetValue("dependencyOnly", out object? token) && Equals(token, true);
        long bytes = dependency ? 0 : checked(extent.ElementCount * TSchema.Descriptor.ElementStride);
        long total = checked(_outputBytes + bytes);
        if (extent.ElementCount <= 0 || total > _requirements.MaxDeviceBytes)
        {
            throw new InvalidOperationException("Stage device budget exceeded before allocation.");
        }

        _outputBytes = total;
        return _scope.PublishPlatform(new Device<TSchema>(new DeviceValue(_run, TSchema.Descriptor, extent, _node, ReserveScratch)));
    }

    /// <summary>Gets the native bindings of an admitted input or this invocation's output.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <param name="value">The invocation-admitted device lease.</param>
    /// <returns>The scope-validated native bindings.</returns>
    public DeviceBindings<TSchema> GetDeviceBindings<TSchema>(ValueLease<Device<TSchema>> value)
        where TSchema : ISchema
    {
        DeviceValue device = GetValue(value);
        int input = _scope.GetInputIndex(value);
        ResourceAccess access = _requirements?.InputAccesses.FirstOrDefault(candidate => candidate.InputIndex == input)?.Access ?? ResourceAccess.ShaderWrite;
        PipelineStage stages = _requirements?.Domain switch
        {
            GraphicsExecutionDomain.Compute => PipelineStage.ComputeShader,
            GraphicsExecutionDomain.Raster => PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ColorOutput,
            GraphicsExecutionDomain.Mesh => PipelineStage.MeshShader | PipelineStage.FragmentShader | PipelineStage.ColorOutput,
            GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download => PipelineStage.Copy,
            _ => PipelineStage.AllCommands,
        };
        if ((access & (ResourceAccess.CopyRead | ResourceAccess.CopyWrite)) != 0)
        {
            stages |= PipelineStage.Copy;
        }

        var scope = new BarrierScope(stages, access);
        device.DeclareSamplers(scope);
        return new DeviceBindings<TSchema>(device, () => GetValue(value), scope);
    }

    /// <summary>Creates an independent use of the existing device value without a transfer.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <param name="value">The admitted device input.</param>
    /// <returns>The independently published use.</returns>
    public ValueLease<Device<TSchema>> ForwardDevice<TSchema>(ValueLease<Device<TSchema>> value)
        where TSchema : ISchema
        => _scope.PublishPlatform(new Device<TSchema>(GetValue(value)));

    /// <summary>Registers one record-only callback; does not record, submit or wait.</summary>
    /// <typeparam name="TSchema">The output schema.</typeparam>
    /// <param name="output">The output owned by this invocation.</param>
    /// <param name="record">The callback retaining all declared input dependencies.</param>
    public void Encode<TSchema>(ValueLease<Device<TSchema>> output, Action<StageRecordContext> record)
        where TSchema : ISchema
    {
        ArgumentNullException.ThrowIfNull(record);
        _scope.ValidateOutput(output);
        DeviceValue value = GetValue(output);
        value.Encode();
        _run.Encode(_node, record);
    }

    /// <summary>Borrows an exact native buffer range through a caller-supplied independent ownership lease.</summary>
    /// <typeparam name="TSchema">The canonical buffer schema.</typeparam>
    /// <typeparam name="TElement">The exact canonical unmanaged element type.</typeparam>
    /// <param name="range">The same-device native range captured by the input snapshot.</param>
    /// <param name="extent">The matching immutable canonical extent.</param>
    /// <param name="ownerLease">An independent use token transferred only on success; never the caller's native allocation owner.</param>
    /// <returns>The projection-local device value with its external manifest identity.</returns>
    public ValueLease<Device<TSchema>> BorrowBuffer<TSchema, TElement>(BufferSlice<TElement> range, SchemaExtent extent, IDisposable ownerLease)
        where TSchema : ISchema
        where TElement : unmanaged
    {
        ArgumentNullException.ThrowIfNull(extent);
        ArgumentNullException.ThrowIfNull(ownerLease);
        if (_requirements is not null)
        {
            throw new InvalidOperationException("Borrowed source projection is not a GPU or transfer operation.");
        }

        var value = new DeviceValue(_run, TSchema.Descriptor, extent, _node);
        value.BindExternalBuffer(range);
        ValueLease<Device<TSchema>> output = _scope.PublishPlatform(new Device<TSchema>(value));
        _run.Own(ownerLease);
        return output;
    }

    /// <summary>Borrows an actual native view through an independent use token without copying or submitting.</summary>
    /// <typeparam name="TSchema">The canonical native-view schema.</typeparam>
    /// <param name="view">The actual same-device view from the immutable input snapshot.</param>
    /// <param name="extent">The immutable canonical source and grid description.</param>
    /// <param name="ownerLease">An independent use token transferred only on success.</param>
    /// <returns>The projection-local native view dependency.</returns>
    public ValueLease<Device<TSchema>> BorrowTexture<TSchema>(IGraphicsTextureView view, SchemaExtent extent, IDisposable ownerLease)
        where TSchema : ISchema
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(extent);
        ArgumentNullException.ThrowIfNull(ownerLease);
        if (_requirements is not null || extent.ElementCount != 1)
        {
            throw new InvalidOperationException("A native source projection requires exactly one borrowed view and no GPU/transfer domain.");
        }

        ValueLease<Device<TSchema>> output = BindExternalTextures<TSchema>([view], extent);
        _run.Own(ownerLease);
        return output;
    }

    /// <summary>Creates a finite per-run argument table retained until actual GPU retirement.</summary>
    /// <param name="description">The explicit logical capacities.</param>
    /// <returns>The borrowed run-owned native table.</returns>
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc description)
    {
        ArgumentNullException.ThrowIfNull(description);
        RequireGpuOperation();
        long slots = checked((long)description.BufferCapacity + description.TextureCapacity + description.SamplerCapacity);
        _scope.ReservePlatformHost(checked(256 + (slots * 128)));
        _run.Validate();
        IArgumentTable table = _run.Device.CreateArgumentTable(description);
        _run.Own(table);
        return table;
    }

    /// <summary>Creates a declared-ABI shader-data scratch buffer without recording or submitting transfers.</summary>
    /// <typeparam name="TArguments">The generated AOT-safe argument element type.</typeparam>
    /// <param name="artifact">The exact artifact declared and retained by this build.</param>
    /// <param name="count">The positive logical element count.</param>
    /// <param name="memory">Automatic device storage or explicit upload staging.</param>
    /// <returns>The borrowed run-owned native buffer.</returns>
    public IGraphicsShaderDataBuffer<TArguments> CreateShaderDataScratchBuffer<TArguments>(StageArtifactLease artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where TArguments : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(artifact);
        RequireGpuOperation();
        if (count == 0 || count > long.MaxValue || TArguments.TransferSize <= 0 || memory is not (MemoryPreference.Automatic or MemoryPreference.Upload) ||
            !ReferenceEquals(_build.GetArtifact(artifact.Description.Key), artifact))
        {
            throw new ArgumentException("Scratch count, memory mode or declared artifact identity mismatch.");
        }

        long bytes = checked((long)count * TArguments.TransferSize);
        long next = checked(_outputBytes + bytes);
        if (next > _requirements!.MaxDeviceBytes)
        {
            throw new InvalidOperationException("Stage device scratch exceeds its finite declaration before allocation.");
        }

        _scope.ReservePlatformHost(256);
        ReserveScratch(bytes);
        if (memory == MemoryPreference.Upload)
        {
            ReserveTransfer(bytes);
        }

        ShaderArtifact shader = artifact.Compute?.Desc.ComputeShader.Artifact ?? artifact.Graphics?.Desc.FragmentShader?.Artifact ??
            artifact.Graphics?.Desc.VertexShader.Artifact ?? artifact.Mesh?.Desc.FragmentShader?.Artifact ?? artifact.Mesh!.Desc.MeshShader.Artifact;
        IGraphicsShaderDataBuffer<TArguments> buffer = _run.Device.CreateShaderDataBuffer<TArguments>(shader, count, memory);
        _run.Own(buffer);
        _outputBytes = next;
        return buffer;
    }

    // One finite invocation-owned credit, consumed only by this stage's normal allocation paths.
    // Reserving the complete peak prevents a later window from failing after earlier native allocations.
    internal void PreflightAllocations(long scratchBytes, long transferBytes)
    {
        RequireGpuOperation();
        if (_preflight || _outputBytes != 0 || scratchBytes < 0 || transferBytes < 0 || transferBytes > scratchBytes || scratchBytes > _requirements!.MaxDeviceBytes)
        {
            throw new InvalidOperationException("Aggregate stage allocation admission must precede all native allocation and fit its declaration.");
        }

        _run.ReserveScratch(scratchBytes);
        _run.ReserveTransfer(transferBytes);
        _scratchCredit = scratchBytes;
        _transferCredit = transferBytes;
        _preflight = true;
    }

    internal void ReserveNativeScratch(long bytes) => ReserveScratch(bytes);

    internal ValueLease<Device<TSchema>> BindExternalTextures<TSchema>(IReadOnlyList<IGraphicsTextureView> views, SchemaExtent extent, IReadOnlyList<IGraphicsSampler>? samplers = null)
        where TSchema : ISchema
    {
        var value = new DeviceValue(_run, TSchema.Descriptor, extent, _node);
        value.BindTextures(views, samplers);
        return _scope.PublishPlatform(new Device<TSchema>(value));
    }

    internal DeviceValue GetValue<TSchema>(ValueLease<Device<TSchema>> value)
        where TSchema : ISchema
    {
        int input = _scope.GetInputIndex(value);
        if (input >= 0 && _requirements is not null && !_requirements.InputAccesses.Any(access => access.InputIndex == input))
        {
            throw new InvalidOperationException("Device input access was not declared by this stage.");
        }

        DeviceValue device = _scope.ReadPlatform(value).Value ?? throw new InvalidOperationException("Uninitialized device placement.");
        if (!device.Descriptor.Matches(TSchema.Descriptor))
        {
            throw new InvalidOperationException("Device schema version, layout or metadata changed after publication.");
        }

        if (!ReferenceEquals(device.Run, _run))
        {
            throw new InvalidOperationException("Device value belongs to another preparation.");
        }

        _run.Validate();
        return device;
    }

    private void ReserveScratch(long bytes)
    {
        if (!_preflight)
        {
            _run.ReserveScratch(bytes);
            return;
        }

        if (bytes < 0 || bytes > _scratchCredit)
        {
            throw new InvalidOperationException("Native allocation exceeds the preflight stage scratch credit.");
        }

        _scratchCredit -= bytes;
    }

    private void ReserveTransfer(long bytes)
    {
        if (!_preflight)
        {
            _run.ReserveTransfer(bytes);
            return;
        }

        if (bytes < 0 || bytes > _transferCredit)
        {
            throw new InvalidOperationException("Native upload exceeds the preflight stage transfer credit.");
        }

        _transferCredit -= bytes;
    }

    private void RequireGpuOperation()
    {
        _scope.ReservePlatformHost(0);
        if (_requirements is null || _requirements.Domain is GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download)
        {
            throw new InvalidOperationException("Native scratch requires a declared GPU operation.");
        }
    }
}
