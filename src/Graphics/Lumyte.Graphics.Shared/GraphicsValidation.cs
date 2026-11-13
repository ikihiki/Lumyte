using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Optional managed diagnostics. Backends leave this state null unless explicitly enabled.</summary>
public sealed class GraphicsValidation
{
    private readonly Dictionary<object, object> _parents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<object>> _uses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _submitted = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(object Command, object Resource), (BarrierScope Scope, bool Used)> _access = [];
    private readonly Dictionary<(object, object, uint, uint), TextureState> _textures = [];
    private readonly HashSet<object> _signals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object[]> _semaphores = new(ReferenceEqualityComparer.Instance);

    /// <summary>Registers an owned child after successful creation.</summary>
    /// <param name="child">The child involved in this explicit diagnostic operation.</param>
    /// <param name="parent">The parent involved in this explicit diagnostic operation.</param>
    public void Register(object child, object parent) => _parents.Add(child, parent);

    /// <summary>Rejects destruction of live parents and command dependencies.</summary>
    /// <param name="resource">The resource involved in this explicit diagnostic operation.</param>
    public void Release(object resource)
    {
        if (_parents.Values.Any(parent => ReferenceEquals(parent, resource)) || _uses.Values.Any(uses => uses.Contains(resource)) || _semaphores.Values.Any(values => values.Contains(resource)))
        {
            throw new InvalidOperationException("Validation: resource has live children or recorded/in-flight uses.");
        }

        _parents.Remove(resource);
    }

    /// <summary>Records a command dependency, including its parent chain.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    /// <param name="resource">The resource involved in this explicit diagnostic operation.</param>
    public void Use(object command, object resource)
    {
        if (!_uses.TryGetValue(command, out HashSet<object>? uses))
        {
            _uses.Add(command, uses = new(ReferenceEqualityComparer.Instance));
        }

        while (uses.Add(resource) && _parents.TryGetValue(resource, out object? parent))
        {
            resource = parent;
        }
    }

    /// <summary>Rejects mapping while commands retain the buffer.</summary>
    /// <param name="resource">The resource involved in this explicit diagnostic operation.</param>
    public void Map(object resource)
    {
        if (_uses.Values.Any(uses => uses.Contains(resource)))
        {
            throw new InvalidOperationException("Validation: mapping conflicts with recorded or in-flight GPU use.");
        }
    }

    /// <summary>Rejects command disposal before completion.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    public void Discard(object command)
    {
        if (_submitted.Contains(command))
        {
            throw new InvalidOperationException("Validation: command is in flight.");
        }

        _uses.Remove(command);
        foreach ((object Command, object Resource) key in _access.Keys.Where(key => ReferenceEquals(key.Command, command)).ToArray())
        {
            _access.Remove(key);
        }

        foreach ((object, object, uint, uint) key in _textures.Keys.Where(key => ReferenceEquals(key.Item1, command)).ToArray())
        {
            _textures.Remove(key);
        }
    }

    /// <summary>Marks successful native submission.</summary>
    /// <param name="command">The command to validate.</param>
    public void Submit(object command) => _submitted.Add(command);

    /// <summary>Retires dependencies after explicit completion observation.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    public void Complete(object command)
    {
        _submitted.Remove(command);
        _uses.Remove(command);
        foreach ((object Command, object Resource) key in _access.Keys.Where(key => ReferenceEquals(key.Command, command)).ToArray())
        {
            _access.Remove(key);
        }

        foreach ((object, object, uint, uint) key in _textures.Keys.Where(key => ReferenceEquals(key.Item1, command)).ToArray())
        {
            _textures.Remove(key);
        }
    }

    /// <summary>Checks a buffer access against the preceding declared dependency.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    /// <param name="resource">The resource involved in this explicit diagnostic operation.</param>
    /// <param name="scope">The scope involved in this explicit diagnostic operation.</param>
    public void Access(object command, object resource, BarrierScope scope)
    {
        if (_access.TryGetValue((command, resource), out (BarrierScope Scope, bool Used) previous) &&
            ((previous.Used && (Writes(previous.Scope.Access) || Writes(scope.Access))) ||
             (!previous.Used && (!Covers(previous.Scope.Access, scope.Access) || !Covers(previous.Scope.Stages, scope.Stages)))))
        {
            throw new InvalidOperationException("Validation: missing barrier or incompatible destination scope.");
        }

        Use(command, resource);
        _access[(command, resource)] = (scope, true);
    }

    /// <summary>Checks a barrier's source scope before publishing its destination scope.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    /// <param name="resource">The resource involved in this explicit diagnostic operation.</param>
    /// <param name="before">The before involved in this explicit diagnostic operation.</param>
    /// <param name="after">The after involved in this explicit diagnostic operation.</param>
    public void Barrier(object command, object resource, BarrierScope before, BarrierScope after)
    {
        Use(command, resource);
        if (_access.TryGetValue((command, resource), out (BarrierScope Scope, bool Used) previous) && (!Covers(before.Access, previous.Scope.Access) || !Covers(before.Stages, previous.Scope.Stages)))
        {
            throw new InvalidOperationException("Validation: barrier source scope does not cover preceding access.");
        }

        _access[(command, resource)] = (after, false);
    }

    /// <summary>Checks each explicitly addressed texture subresource's Before state.</summary>
    /// <param name="command">The command involved in this explicit diagnostic operation.</param>
    /// <param name="barrier">The barrier involved in this explicit diagnostic operation.</param>
    public void TextureBarrier(object command, TextureBarrierDesc barrier)
    {
        Use(command, barrier.Texture);
        TextureSubresourceRange range = barrier.Range;
        for (uint mip = range.BaseMipLevel; mip < checked(range.BaseMipLevel + range.MipLevelCount); mip++)
        {
            for (uint layer = range.BaseArrayLayer; layer < checked(range.BaseArrayLayer + range.ArrayLayerCount); layer++)
            {
                (object Command, object Resource, uint Mip, uint Layer) key = (command, (object)barrier.Texture, mip, layer);
                if (_textures.TryGetValue(key, out TextureState state) && state != barrier.BeforeState)
                {
                    throw new InvalidOperationException("Validation: texture barrier Before state differs from tracked state.");
                }
            }
        }

        for (uint mip = range.BaseMipLevel; mip < checked(range.BaseMipLevel + range.MipLevelCount); mip++)
        {
            for (uint layer = range.BaseArrayLayer; layer < checked(range.BaseArrayLayer + range.ArrayLayerCount); layer++)
            {
                _textures[(command, barrier.Texture, mip, layer)] = barrier.AfterState;
            }
        }
    }

    /// <summary>Validates binary semaphore sequencing before native submission, without mutating state.</summary>
    /// <param name="desc">The desc involved in this explicit diagnostic operation.</param>
    public void ValidateSemaphores(QueueSubmitDesc desc)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
        {
            if (!seen.Add(wait.Semaphore) || !_signals.Contains(wait.Semaphore))
            {
                throw new InvalidOperationException("Validation: duplicate or unsignaled semaphore wait.");
            }
        }

        foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
        {
            if (!seen.Add(signal) || _signals.Contains(signal) || _semaphores.Values.Any(values => values.Contains(signal)))
            {
                throw new InvalidOperationException("Validation: duplicate signal or in-flight semaphore reuse.");
            }
        }
    }

    /// <summary>Commits semaphore state only after successful native submission.</summary>
    /// <param name="submission">The submission involved in this explicit diagnostic operation.</param>
    /// <param name="desc">The desc involved in this explicit diagnostic operation.</param>
    public void SubmittedSemaphores(object submission, QueueSubmitDesc desc)
    {
        foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
        {
            _signals.Remove(wait.Semaphore);
        }

        foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
        {
            _signals.Add(signal);
        }

        _semaphores[submission] = desc.WaitSemaphores.Select(wait => (object)wait.Semaphore).Concat(desc.SignalSemaphores).ToArray();
    }

    /// <summary>Retires semaphore native uses, retaining unconsumed signals.</summary>
    /// <param name="submission">The submission to validate.</param>
    public void CompleteSemaphores(object submission) => _semaphores.Remove(submission);

    private static bool Writes(ResourceAccess access) => (access & (ResourceAccess.CopyWrite | ResourceAccess.ShaderWrite | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilWrite | ResourceAccess.HostWrite)) != 0;

    private static bool Covers(ResourceAccess scope, ResourceAccess access) => (scope & access) == access;

    private static bool Covers(PipelineStage scope, PipelineStage stages) => scope == PipelineStage.AllCommands || (scope & stages) == stages;
}
