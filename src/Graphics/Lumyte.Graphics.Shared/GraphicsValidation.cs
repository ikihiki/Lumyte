using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Explicit per-device CPU-observable diagnostics; no instance exists on the default path.</summary>
public sealed class GraphicsValidation
{
    private readonly Dictionary<object, object> _parents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<object>> _uses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<Operation>> _commands = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<IValidatedShaderReference>> _references = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _submitted = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _released = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _signals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, State> _signalStates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (object Operation, IGraphicsSemaphore? Semaphore)> _acquisitions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<object>> _semaphores = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<HostWriteLease>> _hostWrites = new(ReferenceEqualityComparer.Instance);
    private State _global = new();
    private bool _queueUnknown;

    /// <summary>Starts a diagnostic-only transaction around a native recording operation.</summary>
    /// <param name="command">The recording owner.</param>
    /// <returns>A transaction that rolls back unless committed after native success.</returns>
    public RecordingTransaction BeginRecording(object command) => new(this, command);

    /// <summary>Publishes explicit host access after mapping has excluded GPU users.</summary>
    /// <param name="resource">The buffer allocation.</param>
    /// <param name="access">The host access.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="size">The byte count.</param>
    public void HostAccess(object resource, ResourceAccess access, ulong offset, ulong size)
    {
        if ((access & ResourceAccess.HostWrite) != 0 &&
            _hostWrites.TryGetValue(resource, out List<HostWriteLease>? writes) &&
            size != 0 && writes.Any(write => write.Offset != write.End && offset < write.End && write.Offset < checked(offset + size)))
        {
            throw new InvalidOperationException("Validation: overlapping host writes.");
        }

        ValidateGpuMapping(resource);

        // Actual CPU access happens after explicit GPU retirement. It establishes
        // a new source scope, rather than pretending a GPU barrier performed it.
        State state = _global.Copy();
        Apply(state, new(resource, offset, End(offset, size), new(PipelineStage.Host, access), default, false, null, default, Host: true), false);
        _global = state;
    }

    /// <summary>Validates and retains a host-write range until its callback finishes.</summary>
    /// <param name="resource">The mapped allocation identity.</param>
    /// <param name="offset">The first byte written.</param>
    /// <param name="length">The number of bytes written.</param>
    /// <returns>An idempotent, non-throwing cleanup lease; call only through an enabled validation instance.</returns>
    public HostWriteLease BeginHostWrite(object resource, ulong offset, ulong length)
    {
        ulong end = checked(offset + length);
        if (_hostWrites.TryGetValue(resource, out List<HostWriteLease>? writes) &&
            offset != end && writes.Any(write => write.Offset != write.End && offset < write.End && write.Offset < end))
        {
            throw new InvalidOperationException("Validation: overlapping host writes.");
        }

        // Publish once, before recording the lease: a failed access cannot leak it.
        HostAccess(resource, ResourceAccess.HostWrite, offset, length);
        var lease = new HostWriteLease(this, resource, offset, end);
        if (writes == null)
        {
            _hostWrites.Add(resource, writes = []);
        }

        writes.Add(lease);
        return lease;
    }

    /// <summary>Rejects unmapping while a host-write callback is active.</summary>
    /// <param name="resource">The mapped allocation identity.</param>
    public void ValidateUnmap(object resource) => ValidateNoHostWrites(resource);

    /// <summary>Records metadata read or copy publication in command order.</summary>
    /// <param name="command">The recording owner.</param>
    /// <param name="source">The logical metadata buffer.</param>
    /// <param name="value">The captured snapshot.</param>
    /// <param name="write">Whether this operation publishes a copied value.</param>
    public void Metadata(object command, IShaderDataSource source, IShaderReferenceMetadata value, bool write)
        => Record(command, new(source, 0, 0, default, default, false, null, default, MetadataSource: source, Value: value, MetadataWrite: write));

    /// <summary>Registers a successfully created child.</summary>
    /// <param name="child">The child.</param>
    /// <param name="parent">The owning parent.</param>
    public void Register(object child, object parent)
    {
        _released.Remove(child);
        _parents.Add(child, parent);
    }

    /// <summary>Rejects release while children or recorded/native uses remain.</summary>
    /// <param name="resource">The resource.</param>
    public void Release(object resource)
    {
        ValidateRelease(resource);

        _signals.Remove(resource);
        _signalStates.Remove(resource);
        _parents.Remove(resource);
        _released.Add(resource);
        _global.Buffers.Remove(resource);
        _acquisitions.Remove(resource);
        foreach ((object Resource, uint Mip, uint Layer) key in _global.Textures.Keys.Where(key => ReferenceEquals(key.Resource, resource)).ToArray())
        {
            _global.Textures.Remove(key);
        }
    }

    /// <summary>Checks composite disposal without changing registration or queue state.</summary>
    /// <param name="resource">The resource whose release must be safe.</param>
    public void ValidateRelease(object resource)
    {
        ValidateNoHostWrites(resource);
        if (_parents.Values.Any(parent => ReferenceEquals(parent, resource)) || _uses.Values.Any(uses => uses.Contains(resource)) || _semaphores.Values.Any(uses => uses.Contains(resource)))
        {
            throw new InvalidOperationException("Validation: live child or recorded/in-flight resource use.");
        }
    }

    /// <summary>Retains a diagnostic dependency and its parents, without owning native resources.</summary>
    /// <param name="command">The operation owner.</param>
    /// <param name="resource">The resource.</param>
    public void Use(object command, object resource)
    {
        if (!_uses.TryGetValue(command, out HashSet<object>? uses))
        {
            _uses.Add(command, uses = new(ReferenceEqualityComparer.Instance));
        }

        do
        {
            if (_released.Contains(resource))
            {
                throw new InvalidOperationException("Validation: use after release.");
            }
        }
        while (uses.Add(resource) && _parents.TryGetValue(resource, out resource!));
    }

    /// <summary>Retains registration identities for submission-time revalidation.</summary>
    /// <param name="command">The recording command.</param>
    /// <param name="reference">The opaque registration identity.</param>
    public void Reference(object command, IValidatedShaderReference reference)
    {
        reference.ValidateRegistration();
        if (!_references.TryGetValue(command, out HashSet<IValidatedShaderReference>? references))
        {
            _references.Add(command, references = []);
        }

        references.Add(reference);
    }

    /// <summary>Rejects mapping while recorded or submitted GPU users remain.</summary>
    /// <param name="resource">The mapped resource.</param>
    public void Map(object resource)
    {
        ValidateNoHostWrites(resource);
        ValidateGpuMapping(resource);
    }

    /// <summary>Discards unsubmitted diagnostic state.</summary>
    /// <param name="command">The command.</param>
    public void Discard(object command)
    {
        if (_submitted.Contains(command))
        {
            throw new InvalidOperationException("Validation: command is in flight.");
        }

        Clear(command);
    }

    /// <summary>Marks native submission success without forcing GPU synchronization.</summary>
    /// <param name="command">The command.</param>
    public void Submit(object command) => _submitted.Add(command);

    /// <summary>Retires uses only on observed completion.</summary>
    /// <param name="command">The completed command.</param>
    /// <param name="success">Whether native execution completed successfully.</param>
    public void Complete(object command, bool success = true)
    {
        _queueUnknown |= !success;
        _submitted.Remove(command);
        Clear(command);
    }

    /// <summary>Checks a precisely addressed buffer access.</summary>
    /// <param name="command">The command.</param>
    /// <param name="resource">The allocation.</param>
    /// <param name="scope">The access scope.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="size">The byte count.</param>
    public void Access(object command, object resource, BarrierScope scope, ulong offset = 0, ulong size = ulong.MaxValue)
        => Record(command, new(resource, offset, End(offset, size), scope, default, false, null, default));

    /// <summary>Checks a precisely addressed buffer dependency.</summary>
    /// <param name="command">The command.</param>
    /// <param name="resource">The allocation.</param>
    /// <param name="before">The source scope.</param>
    /// <param name="after">The destination scope.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="size">The byte count.</param>
    public void Barrier(object command, object resource, BarrierScope before, BarrierScope after, ulong offset = 0, ulong size = ulong.MaxValue)
        => Record(command, new(resource, offset, End(offset, size), after, before, true, null, default));

    /// <summary>Records a global memory dependency.</summary>
    /// <param name="command">The command.</param>
    /// <param name="before">The source scope.</param>
    /// <param name="after">The destination scope.</param>
    public void MemoryBarrier(object command, BarrierScope before, BarrierScope after)
        => Record(command, new(null, 0, ulong.MaxValue, after, before, true, null, default));

    /// <summary>Sets the state established by creation or external acquisition.</summary>
    /// <param name="texture">The texture.</param>
    /// <param name="state">The native initial state.</param>
    public void SetTextureState(IGraphicsTexture texture, TextureState state)
    {
        for (uint mip = 0; mip < texture.MipLevels; mip++)
        {
            for (uint layer = 0; layer < texture.ArrayLayers; layer++)
            {
                _global.Textures[(texture, mip, layer)] = new(state, default, false);
            }
        }
    }

    /// <summary>Records a texture transition for exact mip/layer subresources.</summary>
    /// <param name="command">The command.</param>
    /// <param name="barrier">The transition.</param>
    public void TextureBarrier(object command, TextureBarrierDesc barrier)
        => Record(command, new(barrier.Texture, 0, 0, barrier.After, barrier.Before, true, barrier.Range, barrier.AfterState, barrier.BeforeState));

    /// <summary>Checks a texture use against its state and dependency scope.</summary>
    /// <param name="command">The command.</param>
    /// <param name="texture">The allocation.</param>
    /// <param name="range">The subresource range.</param>
    /// <param name="state">The required state.</param>
    /// <param name="scope">The access scope.</param>
    public void TextureAccess(object command, IGraphicsTexture texture, TextureSubresourceRange range, TextureState state, BarrierScope scope)
        => Record(command, new(texture, 0, 0, scope, default, false, range, state));

    /// <summary>Validates ordered commands against a private copy of queue state; failure changes no globals.</summary>
    /// <param name="commands">The submission order.</param>
    /// <returns>The state to publish only after successful native submission.</returns>
    public SubmissionState PrepareSubmission(IReadOnlyList<IGraphicsCommandBuffer> commands) => PrepareSubmission(new QueueSubmitDesc { CommandBuffers = commands });

    /// <summary>Validates queue command order and the memory dependencies supplied by semaphore waits.</summary>
    /// <param name="desc">The pending native submission.</param>
    /// <returns>The private state to publish only on native success.</returns>
    public SubmissionState PrepareSubmission(QueueSubmitDesc desc)
    {
        IReadOnlyList<IGraphicsCommandBuffer> commands = desc.CommandBuffers;
        if (_queueUnknown)
        {
            throw new InvalidOperationException("Validation: queue state is unknown after native execution failure; recreate the device.");
        }

        State state = _global.Copy();
        foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
        {
            ApplySemaphoreWait(state, wait);
        }

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (IGraphicsCommandBuffer command in commands)
        {
            if (!seen.Add(command))
            {
                throw new InvalidOperationException("Validation: duplicate command submission.");
            }

            if (_references.TryGetValue(command, out HashSet<IValidatedShaderReference>? references))
            {
                foreach (IValidatedShaderReference reference in references)
                {
                    reference.ValidateRegistration();
                }
            }

            if (_commands.TryGetValue(command, out List<Operation>? operations))
            {
                foreach (Operation operation in operations)
                {
                    if (operation.Range != null && operation.Resource != null && _acquisitions.TryGetValue(operation.Resource, out (object Operation, IGraphicsSemaphore? Semaphore) acquisition) && _semaphores.ContainsKey(acquisition.Operation))
                    {
                        SemaphoreWaitDesc? wait = desc.WaitSemaphores.FirstOrDefault(value => ReferenceEquals(value.Semaphore, acquisition.Semaphore));
                        if (acquisition.Semaphore == null || wait == null || (wait.Stages & operation.Scope.Stages) != operation.Scope.Stages)
                        {
                            throw new InvalidOperationException("Validation: GPU image use must wait for acquisition at its execution stages.");
                        }
                    }

                    Apply(state, operation, false);
                }
            }
        }

        return new(state);
    }

    /// <summary>Publishes preflight state after native success.</summary>
    /// <param name="state">The prepared state.</param>
    public void CommitSubmission(SubmissionState state)
    {
        state.Value.Metadata.Clear();
        _global = state.Value;
    }

    /// <summary>Records partial native issuance without retiring any outstanding dependency.</summary>
    public void MarkPartialSubmissionFailure() => _queueUnknown = true;

    /// <summary>Publishes one native queue wait only after that operation succeeds.</summary>
    /// <param name="submission">The consuming completion owner.</param>
    /// <param name="semaphore">The successfully consumed signal.</param>
    public void SubmittedWait(object submission, IGraphicsSemaphore semaphore)
    {
        _signals.Remove(semaphore);
        _signalStates.Remove(semaphore);
        SemaphoreUses(submission).Add(semaphore);
    }

    /// <summary>Publishes one native queue signal only after that operation succeeds.</summary>
    /// <param name="submission">The producing completion owner.</param>
    /// <param name="semaphore">The successfully issued signal.</param>
    public void SubmittedSignal(object submission, IGraphicsSemaphore semaphore)
    {
        PresentationSignaled(submission, semaphore);
        _signalStates[semaphore] = _global.Copy();
    }

    /// <summary>Checks queue semaphore sequencing without mutating it.</summary>
    /// <param name="desc">The submission.</param>
    public void ValidateSemaphores(QueueSubmitDesc desc)
    {
        foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
        {
            if (wait.Stages == PipelineStage.None || (wait.Stages & ~PipelineStage.AllCommands) != 0)
            {
                throw new InvalidOperationException("Validation: semaphore waits require valid GPU execution stages.");
            }
        }

        PresentationWaitValidate(desc.WaitSemaphores.Select(wait => wait.Semaphore).ToArray());
        var seen = new HashSet<object>(desc.WaitSemaphores.Select(wait => (object)wait.Semaphore), ReferenceEqualityComparer.Instance);
        foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
        {
            if (!seen.Add(signal))
            {
                throw new InvalidOperationException("Validation: duplicate semaphore.");
            }

            PresentationSignalValidate(signal);
        }
    }

    /// <summary>Publishes semaphore transitions after native success.</summary>
    /// <param name="submission">The submission identity.</param>
    /// <param name="desc">The submission.</param>
    public void SubmittedSemaphores(object submission, QueueSubmitDesc desc)
    {
        foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
        {
            SubmittedWait(submission, wait.Semaphore);
        }

        foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
        {
            SubmittedSignal(submission, signal);
        }
    }

    /// <summary>Checks an acquisition or queue signal before native execution.</summary>
    /// <param name="semaphore">The semaphore.</param>
    public void PresentationSignalValidate(IGraphicsSemaphore semaphore)
    {
        if (_released.Contains(semaphore) || _signals.Contains(semaphore) || _semaphores.Values.Any(uses => uses.Contains(semaphore)))
        {
            throw new InvalidOperationException("Validation: semaphore signal is unconsumed or in flight.");
        }
    }

    /// <summary>Publishes a successful native signal and retains its operation.</summary>
    /// <param name="operation">The native operation.</param>
    /// <param name="semaphore">The semaphore.</param>
    public void PresentationSignaled(object operation, IGraphicsSemaphore semaphore)
    {
        _signals.Add(semaphore);
        _signalStates.Remove(semaphore);
        SemaphoreUses(operation).Add(semaphore);
    }

    /// <summary>Associates a newly acquired image with its native readiness operation.</summary>
    /// <param name="operation">The acquisition completion token.</param>
    /// <param name="texture">The borrowed image.</param>
    /// <param name="semaphore">The acquisition signal, or null for explicit native completion waiting.</param>
    public void AcquireTexture(object operation, IGraphicsTexture texture, IGraphicsSemaphore? semaphore)
    {
        _acquisitions[texture] = (operation, semaphore);
        _ = SemaphoreUses(operation);
    }

    /// <summary>Checks submitted image state and the dependency on its producing submission.</summary>
    /// <param name="texture">The acquired image.</param>
    /// <param name="waits">The presentation waits.</param>
    public void ValidatePresentTexture(IGraphicsTexture texture, IReadOnlyList<IGraphicsSemaphore>? waits = null)
    {
        if (!_global.Textures.TryGetValue((texture, 0, 0), out TextureAccessState? state) || state.State != TextureState.Present)
        {
            throw new InvalidOperationException("Validation: transition the acquired image to Present before presenting.");
        }

        bool inFlight = _submitted.Any(command => _uses.TryGetValue(command, out HashSet<object>? uses) && uses.Contains(texture));
        if (inFlight && !(waits ?? []).Any(semaphore => _signals.Contains(semaphore) && _signalStates.TryGetValue(semaphore, out State? produced) && produced.Textures.TryGetValue((texture, 0, 0), out TextureAccessState? value) && ReferenceEquals(value, state)))
        {
            throw new InvalidOperationException("Validation: presentation must wait for the image-producing submission or observe its completion.");
        }
    }

    /// <summary>Checks distinct, previously signaled presentation waits.</summary>
    /// <param name="semaphores">The waits.</param>
    public void PresentationWaitValidate(IReadOnlyList<IGraphicsSemaphore> semaphores)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (IGraphicsSemaphore semaphore in semaphores)
        {
            if (!seen.Add(semaphore) || !_signals.Contains(semaphore) || _released.Contains(semaphore))
            {
                throw new InvalidOperationException("Validation: duplicate or unsignaled semaphore wait.");
            }
        }
    }

    /// <summary>Publishes successful native waits, merging partial operations.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="semaphores">The consumed waits.</param>
    public void PresentationWaited(object operation, IReadOnlyList<IGraphicsSemaphore> semaphores)
    {
        foreach (IGraphicsSemaphore semaphore in semaphores)
        {
            _signals.Remove(semaphore);
            _signalStates.Remove(semaphore);
            SemaphoreUses(operation).Add(semaphore);
        }
    }

    /// <summary>Retires native semaphore uses only after actual completion.</summary>
    /// <param name="submission">The completed operation.</param>
    public void CompleteSemaphores(object submission) => _semaphores.Remove(submission);

    private static ulong End(ulong offset, ulong size) => size == ulong.MaxValue ? ulong.MaxValue : checked(offset + size);

    private static bool Writes(ResourceAccess access) => (access & (ResourceAccess.CopyWrite | ResourceAccess.ShaderWrite | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilWrite | ResourceAccess.HostWrite)) != 0;

    private static bool Covers(BarrierScope scope, BarrierScope access) => (scope.Access & access.Access) == access.Access && (scope.Stages & access.Stages) == access.Stages;

    private static bool CoversSource(BarrierScope before, BarrierScope previous)
    {
        const ResourceAccess WritesMask = ResourceAccess.CopyWrite | ResourceAccess.ShaderWrite | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilWrite | ResourceAccess.HostWrite;

        // Read-after-write needs memory availability for writes; write-after-read
        // needs execution ordering, not a fictitious read cache flush.
        return Covers(before, new(previous.Stages, previous.Access & WritesMask));
    }

    private static AccessState Transition(AccessState previous, Operation operation)
    {
        if (operation.Host && operation.Scope.Access == ResourceAccess.HostWrite)
        {
            return new(operation.Scope, true);
        }

        if (operation.Barrier)
        {
            BarrierScope required = previous.Used ? previous.Scope : previous.Available ?? default;
            if ((previous.Used && !CoversSource(operation.Before, required)) ||
                (!previous.Used && previous.Available != null && (operation.Before.Stages & required.Stages) == 0))
            {
                throw new InvalidOperationException("Validation: barrier Before scope misses preceding access/dependency.");
            }

            return new(operation.Scope, false, operation.Scope);
        }

        if (previous.Available is { } available && !Covers(available, operation.Scope))
        {
            throw new InvalidOperationException("Validation: destination scope does not cover this access.");
        }

        if (previous.Used && (Writes(previous.Scope.Access) || Writes(operation.Scope.Access)))
        {
            throw new InvalidOperationException("Validation: missing barrier between conflicting accesses.");
        }

        BarrierScope scope = previous.Used ? new(previous.Scope.Stages | operation.Scope.Stages, previous.Scope.Access | operation.Scope.Access) : operation.Scope;
        return new(scope, true, previous.Available);
    }

    private static void Apply(State state, Operation operation, bool recording)
    {
        if (operation.MetadataSource is { } source)
        {
            if (!operation.MetadataWrite)
            {
                if (!state.Metadata.TryGetValue(source, out IShaderReferenceMetadata? current) &&
                    !source.TryGetDependencies(out current))
                {
                    throw new InvalidOperationException("Validation: shader buffer dependency metadata is unavailable or unknown.");
                }

                if (current == null || !ReferenceEquals(current, operation.Value))
                {
                    throw new InvalidOperationException("Validation: shader buffer metadata sequencing changed after command recording.");
                }
            }

            state.Metadata[source] = operation.Value!;
            return;
        }

        if (operation.Resource == null)
        {
            foreach (object resource in state.Buffers.Keys.ToArray())
            {
                state.Buffers[resource] = state.Buffers[resource].Select(previous =>
                {
                    if (previous.Used ? !CoversSource(operation.Before, previous.Scope) : previous.Available == null || (operation.Before.Stages & previous.Available.Value.Stages) == 0)
                    {
                        return previous;
                    }

                    AccessState next = Transition(new(previous.Scope, previous.Used, previous.Available), operation);
                    return previous with { Scope = next.Scope, Used = next.Used, Available = next.Available };
                }).ToList();
            }

            foreach ((object Resource, uint Mip, uint Layer) key in state.Textures.Keys.ToArray())
            {
                TextureAccessState previous = state.Textures[key];
                if (previous.Used ? CoversSource(operation.Before, previous.Scope) : previous.Available != null && (operation.Before.Stages & previous.Available.Value.Stages) != 0)
                {
                    AccessState next = Transition(new(previous.Scope, previous.Used, previous.Available), operation);
                    state.Textures[key] = previous with { Scope = next.Scope, Used = next.Used, Available = next.Available };
                }
            }

            return;
        }

        if (operation.Range is { } range)
        {
            for (uint mip = range.BaseMipLevel; mip < checked(range.BaseMipLevel + range.MipLevelCount); mip++)
            {
                for (uint layer = range.BaseArrayLayer; layer < checked(range.BaseArrayLayer + range.ArrayLayerCount); layer++)
                {
                    (object Resource, uint Mip, uint Layer) key = (operation.Resource, mip, layer);
                    bool known = state.Textures.TryGetValue(key, out TextureAccessState? previous);
                    previous ??= new(TextureState.Undefined, default, false);
                    TextureState expected = operation.Barrier ? operation.BeforeState : operation.TextureState;
                    if ((!recording || known) && previous.State != expected)
                    {
                        throw new InvalidOperationException("Validation: texture Before/use state differs from queue state.");
                    }

                    AccessState next = Transition(new(previous.Scope, previous.Used, previous.Available), operation);
                    state.Textures[key] = new(operation.TextureState, next.Scope, next.Used, next.Available);
                }
            }

            return;
        }

        if (!state.Buffers.TryGetValue(operation.Resource, out List<BufferAccess>? ranges))
        {
            state.Buffers.Add(operation.Resource, ranges = []);
        }

        var nextRanges = new List<BufferAccess>();
        var cuts = new SortedSet<ulong> { operation.Offset, operation.End };
        foreach (BufferAccess previous in ranges)
        {
            if (previous.End <= operation.Offset || previous.Offset >= operation.End)
            {
                nextRanges.Add(previous);
                continue;
            }

            cuts.Add(Math.Max(previous.Offset, operation.Offset));
            cuts.Add(Math.Min(previous.End, operation.End));
            if (previous.Offset < operation.Offset)
            {
                nextRanges.Add(previous with { End = operation.Offset });
            }

            if (previous.End > operation.End)
            {
                nextRanges.Add(previous with { Offset = operation.End });
            }
        }

        ulong[] points = cuts.ToArray();
        for (int index = 0; index < points.Length - 1; index++)
        {
            BufferAccess? previous = ranges.FirstOrDefault(value => value.Offset <= points[index] && value.End >= points[index + 1]);
            AccessState next = Transition(previous == null ? new(default, false) : new(previous.Scope, previous.Used, previous.Available), operation);
            nextRanges.Add(new(points[index], points[index + 1], next.Scope, next.Used, next.Available));
        }

        state.Buffers[operation.Resource] = nextRanges;
    }

    private void ValidateNoHostWrites(object resource)
    {
        if (_hostWrites.ContainsKey(resource))
        {
            throw new InvalidOperationException("Validation: allocation has an active host-write callback.");
        }
    }

    private void ValidateGpuMapping(object resource)
    {
        if (_released.Contains(resource))
        {
            throw new InvalidOperationException("Validation: use after release.");
        }

        if (_uses.Values.Any(uses => uses.Contains(resource)))
        {
            throw new InvalidOperationException("Validation: CPU mapping conflicts with GPU use.");
        }
    }

    private void Record(object command, Operation operation)
    {
        if (!_commands.TryGetValue(command, out List<Operation>? operations))
        {
            _commands.Add(command, operations = []);
        }

        var local = new State();
        foreach (Operation previous in operations)
        {
            Apply(local, previous, true);
        }

        Apply(local, operation, true);
        if (operation.Resource != null)
        {
            Use(command, operation.Resource);
        }

        operations.Add(operation);
    }

    private void ApplySemaphoreWait(State state, SemaphoreWaitDesc wait)
    {
        if (!_signals.Contains(wait.Semaphore))
        {
            throw new InvalidOperationException("Validation: semaphore wait has no preceding signal.");
        }

        if (!_signalStates.TryGetValue(wait.Semaphore, out State? signal))
        {
            return;
        }

        const ResourceAccess DeviceAccess = ResourceAccess.CopyRead | ResourceAccess.CopyWrite | ResourceAccess.IndexRead | ResourceAccess.IndirectRead | ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite;
        var destination = new BarrierScope(wait.Stages, DeviceAccess);
        foreach ((object resource, List<BufferAccess> ranges) in state.Buffers.ToArray())
        {
            if (signal.Buffers.TryGetValue(resource, out List<BufferAccess>? produced))
            {
                state.Buffers[resource] = ranges.Select(access => produced.Any(value => ReferenceEquals(value, access)) ? access with { Scope = destination, Used = false, Available = destination } : access).ToList();
            }
        }

        foreach ((object Resource, uint Mip, uint Layer) key in state.Textures.Keys.ToArray())
        {
            TextureAccessState access = state.Textures[key];
            if (signal.Textures.TryGetValue(key, out TextureAccessState? produced) && ReferenceEquals(produced, access))
            {
                state.Textures[key] = access with { Scope = destination, Used = false, Available = destination };
            }
        }
    }

    private HashSet<object> SemaphoreUses(object operation)
    {
        if (!_semaphores.TryGetValue(operation, out HashSet<object>? uses))
        {
            _semaphores.Add(operation, uses = new(ReferenceEqualityComparer.Instance));
        }

        return uses;
    }

    private void Clear(object command)
    {
        _uses.Remove(command);
        _commands.Remove(command);
        _references.Remove(command);
    }

    /// <summary>A validation-only host-write lifetime. Disposal only removes tracking and never validates resource state.</summary>
    public sealed class HostWriteLease : IDisposable
    {
        private readonly object _resource;
        private GraphicsValidation? _owner;

        internal HostWriteLease(GraphicsValidation owner, object resource, ulong offset, ulong end)
        {
            _owner = owner;
            _resource = resource;
            Offset = offset;
            End = end;
        }

        internal ulong Offset { get; }

        internal ulong End { get; }

        /// <summary>Ends tracking once without masking a callback exception.</summary>
        public void Dispose()
        {
            GraphicsValidation? owner = _owner;
            _owner = null;
            if (owner != null && owner._hostWrites.TryGetValue(_resource, out List<HostWriteLease>? writes))
            {
                writes.Remove(this);
                if (writes.Count == 0)
                {
                    owner._hostWrites.Remove(_resource);
                }
            }
        }
    }

    /// <summary>Optional recording rollback state; no native resource ownership is changed.</summary>
    public sealed class RecordingTransaction : IDisposable
    {
        private readonly GraphicsValidation _owner;
        private readonly object _command;
        private readonly List<Operation>? _operations;
        private readonly HashSet<object>? _uses;
        private readonly HashSet<IValidatedShaderReference>? _references;
        private bool _committed;

        internal RecordingTransaction(GraphicsValidation owner, object command)
        {
            _owner = owner;
            _command = command;
            _operations = owner._commands.TryGetValue(command, out List<Operation>? operations) ? new(operations) : null;
            _uses = owner._uses.TryGetValue(command, out HashSet<object>? uses) ? new(uses, ReferenceEqualityComparer.Instance) : null;
            _references = owner._references.TryGetValue(command, out HashSet<IValidatedShaderReference>? references) ? new(references) : null;
        }

        /// <summary>Accepts diagnostics after native recording succeeds.</summary>
        public void Commit() => _committed = true;

        /// <summary>Restores pre-call diagnostics on failed recording.</summary>
        public void Dispose()
        {
            if (_committed)
            {
                return;
            }

            _owner.Clear(_command);
            if (_operations != null)
            {
                _owner._commands.Add(_command, _operations);
            }

            if (_uses != null)
            {
                _owner._uses.Add(_command, _uses);
            }

            if (_references != null)
            {
                _owner._references.Add(_command, _references);
            }
        }
    }

    /// <summary>Private prepared queue state, published only on native success.</summary>
    public sealed class SubmissionState
    {
        internal SubmissionState(State value)
        {
            Value = value;
        }

        internal State Value { get; }
    }

    internal sealed record BufferAccess(ulong Offset, ulong End, BarrierScope Scope, bool Used, BarrierScope? Available = null);

    internal sealed record TextureAccessState(TextureState State, BarrierScope Scope, bool Used, BarrierScope? Available = null);

    internal sealed class State
    {
        internal Dictionary<IShaderDataSource, IShaderReferenceMetadata> Metadata { get; } = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<object, List<BufferAccess>> Buffers { get; } = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<(object Resource, uint Mip, uint Layer), TextureAccessState> Textures { get; } = [];

        internal State Copy()
        {
            var result = new State();
            foreach ((object resource, List<BufferAccess> accesses) in Buffers)
            {
                result.Buffers.Add(resource, new(accesses));
            }

            foreach (KeyValuePair<(object Resource, uint Mip, uint Layer), TextureAccessState> pair in Textures)
            {
                result.Textures.Add(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<IShaderDataSource, IShaderReferenceMetadata> pair in Metadata)
            {
                result.Metadata.Add(pair.Key, pair.Value);
            }

            return result;
        }
    }

    private sealed record Operation(object? Resource, ulong Offset, ulong End, BarrierScope Scope, BarrierScope Before, bool Barrier, TextureSubresourceRange? Range, TextureState TextureState, TextureState BeforeState = default, bool Host = false, IShaderDataSource? MetadataSource = null, IShaderReferenceMetadata? Value = null, bool MetadataWrite = false);

    private sealed record AccessState(BarrierScope Scope, bool Used, BarrierScope? Available = null);
}
