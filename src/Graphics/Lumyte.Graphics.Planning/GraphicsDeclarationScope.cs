using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A finite, expiring stage view of a common frame declaration; it cannot allocate native objects or submit work.</summary>
public sealed class GraphicsDeclarationScope
{
    private readonly GraphicsPlanBuilder _plan;
    private readonly Action _verify;
    private readonly GraphicsStageRequirement _quota;
    private readonly List<PlanPass> _declaredPasses = [];
    private readonly HashSet<PlanPass> _inputProducers = [];
    private int _passes;
    private int _resources;

    internal GraphicsDeclarationScope(GraphicsPlanBuilder plan, Action verify, GraphicsStageRequirement quota)
    {
        (_plan, _verify, _quota) = (plan, verify, quota);
    }

    /// <summary>Declares one pass within the invocation reservation.</summary>
    /// <param name = "name">The diagnostic name.</param>
    /// <returns>An expiring pass view, never the root pass authority.</returns>
    public GraphicsPassScope AddPass(string name)
    {
        Validate();
        if (_passes >= _quota.MaximumPasses)
        {
            throw new InvalidOperationException("Stage pass reservation exceeded before declaration.");
        }

        _passes++;
        PlanPass pass = _plan.AddPass(name);
        foreach (PlanPass producer in _inputProducers)
        {
            pass.DependsOn(producer);
        }

        _declaredPasses.Add(pass);
        return new(this, pass);
    }

    /// <summary>Declares a transient typed buffer before native preparation.</summary>
    /// <typeparam name = "T">The unmanaged element.</typeparam>
    /// <param name = "description">The immutable native requirements.</param>
    /// <returns>The initial undefined version.</returns>
    public PlanBuffer<T> CreateBuffer<T>(BufferDesc<T> description)
        where T : unmanaged
    {
        ReserveResource();
        return _plan.CreateBuffer(description);
    }

    /// <summary>Declares a transient texture before native preparation.</summary>
    /// <param name = "description">The immutable native requirements.</param>
    /// <returns>The initial undefined version.</returns>
    public PlanTexture CreateTexture(TextureDesc description)
    {
        ReserveResource();
        return _plan.CreateTexture(description);
    }

    /// <summary>Declares view.</summary>
    /// <param name = "source">The source.</param>
    /// <param name = "description">The description.</param>
    /// <returns>The declared result.</returns>
    public PlanTexture View(PlanTexture source, TextureViewDesc description)
    {
        ReserveResource();
        return _plan.View(source, description);
    }

    /// <summary>Declares finite typed shader-data storage through this expiring stage reservation.</summary>
    /// <typeparam name="T">The shader element ABI.</typeparam>
    /// <param name="artifact">The immutable compiled layout authority.</param>
    /// <param name="count">The finite logical capacity.</param>
    /// <param name="memory">The explicit memory purpose.</param>
    /// <returns>The undefined storage declaration.</returns>
    public PlanShaderData<T> CreateShaderData<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments
    {
        ReserveResource();
        return _plan.CreateShaderData<T>(artifact, count, memory);
    }

    internal void ReserveTransferResource() => ReserveResource();

    internal void RegisterInputProducer(PlanPass? producer)
    {
        Validate();
        if (producer is null || !_inputProducers.Add(producer))
        {
            return;
        }

        foreach (PlanPass pass in _declaredPasses)
        {
            pass.DependsOn(producer);
        }
    }

    internal void Validate()
    {
        _verify.Invoke();
        _plan.Mutable();
    }

    private void ReserveResource()
    {
        Validate();
        if (_resources >= _quota.MaximumResources)
        {
            throw new InvalidOperationException("Stage resource reservation exceeded before declaration.");
        }

        _resources++;
    }
}
