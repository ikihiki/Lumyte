using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsRun(IGraphicDevice device, GraphicsDeviceGeneration generation, GraphicsPipelinePolicy policy, int[]? nodeEpochs = null, GraphicsScratchBudget? sharedBudget = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly GraphicsPipelinePolicy _policy = policy;
    private readonly GraphicsScratchBudget _budget = sharedBudget ?? new GraphicsScratchBudget(policy.MaxScratchBytes);
    private readonly List<IDisposable> _owners = [];
    private readonly List<PipelineExternalResource> _external = [];
    private readonly Dictionary<object, int> _writes = [];
    private readonly SortedDictionary<int, Action<StageRecordContext>> _records = [];
    private readonly int[] _nodeEpochs = nodeEpochs ?? [];
    private readonly bool[] _prepared = new bool[nodeEpochs?.Length ?? 0];
    private readonly Dictionary<int, TaskCompletionSource> _epochReady = [];
    private readonly SortedDictionary<int, Func<ValueTask>> _readbacks = [];
    private readonly SortedDictionary<int, Action> _publications = [];
    private readonly List<Action<Exception>> _abortReadbacks = [];
    private readonly List<Action> _recordValidations = [];
    private long _scratch;
    private long _transfer;
    private bool _disposed;
    private long _readbackBytes;
    private Exception? _abortError;

    internal IGraphicDevice Device { get; } = device;

    internal GraphicsDeviceGeneration Generation { get; } = generation;

    internal int EpochCount => _nodeEpochs.Length == 0 ? 1 : _nodeEpochs.Max() + 1;

    internal bool HasReadbacks => _readbacks.Count != 0;

    internal Func<int, ValueTask>? BeforeReadbackMap { get; set; }

    public void Dispose()
    {
        lock (_gate)
        {
            var errors = new List<Exception>();
            for (int i = _owners.Count - 1; i >= 0; i--)
            {
                try
                {
                    _owners[i].Dispose();
                    _owners.RemoveAt(i);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException(errors);
            }

            _records.Clear();
            _readbacks.Clear();
            _publications.Clear();
            _abortReadbacks.Clear();
            _recordValidations.Clear();
            _external.Clear();
            BeforeReadbackMap = null;
            _budget.Release(_scratch);
            _scratch = 0;
            _disposed = true;
        }
    }

    internal GraphicsPipelineResourceManifest CaptureManifest() => new(_external.Where(resource => resource.Scope.Access != ResourceAccess.None).OrderBy(resource => resource.Order));

    internal PipelineTextureResource BindTexture(IGraphicsTextureView view, TextureState state, BarrierScope scope, long order)
    {
        Validate();
        if (view is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, Device))
        {
            throw new ArgumentException("The external view has no matching actual native device origin.", nameof(view));
        }

        origin.ValidateOrigin();
        var resource = new PipelineTextureResource(Device, Generation, view, state, scope) { Order = order };
        lock (_gate)
        {
            _external.Add(resource);
        }

        return resource;
    }

    internal PipelineBufferResource<TElement> BindBuffer<TElement>(BufferSlice<TElement> slice, long order)
        where TElement : unmanaged
    {
        Validate();
        if (slice.Buffer is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, Device))
        {
            throw new ArgumentException("The borrowed buffer belongs to another actual native device.", nameof(slice));
        }

        origin.ValidateOrigin();
        var resource = new PipelineBufferResource<TElement>(Device, Generation, slice, default) { Order = order };
        lock (_gate)
        {
            _external.Add(resource);
        }

        return resource;
    }

    internal void ClaimWrite(object allocationIdentity, int node)
    {
        lock (_gate)
        {
            if (_writes.TryGetValue(allocationIdentity, out int existing) && existing != node)
            {
                throw new InvalidOperationException("Unordered writers consume the same native target version.");
            }

            _writes[allocationIdentity] = node;
        }
    }

    internal void BindSampler(IGraphicsSampler sampler, BarrierScope scope, long order)
    {
        Validate();
        if (sampler is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, Device))
        {
            throw new ArgumentException("The sampler has no matching actual native device origin.", nameof(sampler));
        }

        origin.ValidateOrigin();
        lock (_gate)
        {
            PipelineSamplerResource? existing = _external.OfType<PipelineSamplerResource>().FirstOrDefault(resource => ReferenceEquals(resource.Sampler, sampler));
            if (existing is not null)
            {
                existing.AddAccess(scope);
            }
            else
            {
                _external.Add(new PipelineSamplerResource(Device, Generation, sampler, scope) { Order = order });
            }
        }
    }

    internal void Prepared(int node)
    {
        lock (_gate)
        {
            if (_prepared[node])
            {
                return;
            }

            _prepared[node] = true;
            int epoch = _nodeEpochs[node];
            if (Enumerable.Range(0, _prepared.Length).All(index => _nodeEpochs[index] != epoch || _prepared[index]))
            {
                EpochSignal(epoch).TrySetResult();
            }
        }
    }

    internal Task WaitForEpochAsync(int epoch)
    {
        lock (_gate)
        {
            return EpochSignal(epoch).Task;
        }
    }

    internal void RegisterReadback(int node, Func<ValueTask> map, Action<Exception> abort, Action publish)
    {
        lock (_gate)
        {
            _readbacks.Add(node, map);
            _publications.Add(node, publish);
            _abortReadbacks.Add(abort);
            if (_abortError is not null)
            {
                abort(_abortError);
            }
        }

        Prepared(node);
    }

    internal async ValueTask MapEpochAsync(int epoch)
    {
        foreach ((int node, Func<ValueTask> map) in _readbacks.ToArray())
        {
            if (_nodeEpochs[node] == epoch)
            {
                if (BeforeReadbackMap is not null)
                {
                    await BeforeReadbackMap(node);
                }

                await map();
            }
        }
    }

    internal void PublishEpoch(int epoch, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate();
            foreach (Action publish in _publications.Where(pair => _nodeEpochs[pair.Key] == epoch).Select(pair => pair.Value).ToArray())
            {
                publish();
            }
        }
    }

    internal bool HasRecordEpoch(int epoch) => _records.Keys.Any(node => (node < 0 ? 0 : _nodeEpochs[node]) == epoch);

    internal void AbortContinuations(Exception error)
    {
        lock (_gate)
        {
            _abortError ??= error;
            foreach (Action<Exception> abort in _abortReadbacks)
            {
                abort(_abortError);
            }
        }
    }

    internal void ReserveReadback(long bytes)
    {
        lock (_gate)
        {
            long next = checked(_readbackBytes + bytes);
            if (bytes < 0 || next > _policy.MaxReadbackBytes)
            {
                throw new InvalidOperationException("Graphics readback budget exceeded before allocation.");
            }

            _readbackBytes = next;
        }
    }

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? aborted = Volatile.Read(ref _abortError);
        if (aborted is not null)
        {
            throw new InvalidOperationException("The execution has stopped; no further CPU/GPU interval may be prepared.", aborted);
        }

        Generation.ValidateDevice(Device);
        if (Device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The compiled device generation is no longer usable.");
        }
    }

    internal void Own(IDisposable owner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owners.Add(owner);
        }
    }

    internal void ReserveScratch(long bytes)
    {
        lock (_gate)
        {
            Validate();
            long next = checked(_scratch + bytes);
            if (bytes < 0 || next > _policy.MaxScratchBytes)
            {
                throw new InvalidOperationException("Graphics scratch budget exceeded before allocation.");
            }

            _budget.Reserve(bytes);
            _scratch = next;
        }
    }

    internal void ReserveTransfer(long bytes)
    {
        lock (_gate)
        {
            Validate();
            long next = checked(_transfer + bytes);
            if (bytes < 0 || next > _policy.MaxTransferBytes)
            {
                throw new InvalidOperationException("Graphics transfer budget exceeded before allocation.");
            }

            _transfer = next;
        }
    }

    internal void Encode(int node, Action<StageRecordContext> record)
    {
        lock (_gate)
        {
            Validate();
            if (!_records.TryAdd(node, record))
            {
                throw new InvalidOperationException("A node can register only one record callback.");
            }
        }
    }

    internal void AddRecordValidation(Action validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        lock (_gate)
        {
            Validate();
            _recordValidations.Add(validate);
        }
    }

    internal void ValidateRecording(IGraphicsCommandBuffer commands)
    {
        Validate();
        foreach (Action validate in _recordValidations)
        {
            validate();
        }

        if (commands is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, Device))
        {
            throw new ArgumentException("The command belongs to another actual native device.", nameof(commands));
        }

        origin.ValidateOrigin();
        if (commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("A pipeline requires an idle recording command buffer.");
        }
    }

    internal void Record(IGraphicsCommandBuffer commands) => RecordEpoch(commands, null);

    internal void RecordEpoch(IGraphicsCommandBuffer commands, int? epoch)
    {
        ValidateRecording(commands);
        if (commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("A pipeline requires an idle recording command buffer.");
        }

        foreach ((int node, Action<StageRecordContext> record) in _records)
        {
            if (epoch is not null && (node < 0 ? 0 : _nodeEpochs[node]) != epoch)
            {
                continue;
            }

            var scope = new RecordCommandScope(commands);
            try
            {
                record(new StageRecordContext(scope));
            }
            catch (Exception primary)
            {
                try
                {
                    scope.CloseOwnedEncoder();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(primary, cleanup);
                }

                throw;
            }

            scope.CloseOwnedEncoder();
            if (commands.State != CommandBufferState.Recording)
            {
                throw new InvalidOperationException("A stage finalized or consumed the borrowed command.");
            }
        }
    }

    private TaskCompletionSource EpochSignal(int epoch)
    {
        if (!_epochReady.TryGetValue(epoch, out TaskCompletionSource? signal))
        {
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _epochReady.Add(epoch, signal);
        }

        return signal;
    }
}
