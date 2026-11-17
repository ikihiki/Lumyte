using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides a borrowed device and target geometry without a RenderGraph.</summary>
public sealed class RenderPreparationContext2D
{
    private readonly DrawingResourceState2D _resources;
    private readonly bool _allowReturnedResources;
    private long _otherWorkingBytes;
    private PreparationWorkingBytes2D? _otherWorkingBytesSource;

    internal RenderPreparationContext2D(IGraphicDevice device, DrawingTargetInfo2D target, DrawingResourceState2D resources, DrawingArgumentPlan2D? argumentPlan = null, bool allowReturnedResources = false)
    {
        Device = device;
        Target = target;
        _resources = resources;
        _allowReturnedResources = allowReturnedResources;
        ArgumentPlan = argumentPlan ?? new(device);
        Clips = new(target);
        ArgumentPlan.RegisterPreparationCache(Clips);
    }

    /// <summary>Gets the borrowed device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the borrowed pipeline and optional shared sampler.</summary>
    public DrawingResources2D Resources => _resources.Resources;

    /// <summary>Gets the borrowed preparation-local registration plan; custom renderers participate explicitly.</summary>
    public DrawingArgumentPlan2D ArgumentPlan { get; }

    /// <summary>Gets the target geometry normalized to Load.</summary>
    public DrawingTargetInfo2D Target { get; }

    internal bool SupportsCanonicalRecords => _resources.SupportsCanonicalRecords;

    internal bool ComputeShapeMembership { get; init; }

    internal Pipelines.CoverageMembershipService2D? MembershipService { get; init; }

    internal ClipNormalizationCache2D Clips { get; }

    internal bool EnforceWorkingBudget { get; init; }

    internal bool HasWorkingBytesSource => _otherWorkingBytesSource is { HasSource: true };

    internal long MaxWorkingBytes { get; init; } = 256L * 1024 * 1024;

    // Budgeted forks request cumulative accounting when they read this value.
    // Legacy reads stay inert. The source
    // is memoized per group, and context forks preserve it without forcing it.
    internal long OtherWorkingBytes
    {
        get
        {
            if (!EnforceWorkingBudget)
            {
                return _otherWorkingBytes;
            }

            if (_otherWorkingBytesSource is { } source)
            {
                long bytes = source.Value;
                _otherWorkingBytes = bytes;
                _otherWorkingBytesSource = null;
            }

            return _otherWorkingBytes;
        }

        set
        {
            _otherWorkingBytes = value;
            _otherWorkingBytesSource = null;
        }
    }

    /// <summary>Retains the renderer's existing resource owner for prepared work through GPU completion.</summary>
    /// <returns>A lifetime token containing no pipeline or shader state.</returns>
    public IDisposable RetainResources() => _resources.Retain(_allowReturnedResources);

    /// <summary>Rejects resources whose runtime generation is no longer usable.</summary>
    public void ValidateResources() => _resources.Validate();

    /// <summary>Creates a borrowed context using another same-device argument owner, preserving target and resource lifetime.</summary>
    /// <param name="argumentPlan">The borrowed registration and default-resource owner.</param>
    /// <returns>The context for explicitly participating preparation.</returns>
    public RenderPreparationContext2D WithArgumentPlan(DrawingArgumentPlan2D argumentPlan)
    {
        ArgumentNullException.ThrowIfNull(argumentPlan);
        if (!ReferenceEquals(Device, argumentPlan.Device))
        {
            throw new ArgumentException("The argument plan belongs to another device.", nameof(argumentPlan));
        }

        return CopyWorkingBudget(new(Device, Target, _resources, argumentPlan, _allowReturnedResources) { EnforceWorkingBudget = EnforceWorkingBudget, MaxWorkingBytes = MaxWorkingBytes, ComputeShapeMembership = ComputeShapeMembership, MembershipService = MembershipService });
    }

    internal RenderPreparationContext2D WithWorkingLimit(long maximum)
        => CopyWorkingBudget(new(Device, Target, _resources, ArgumentPlan, _allowReturnedResources) { EnforceWorkingBudget = true, MaxWorkingBytes = Math.Min(MaxWorkingBytes, maximum), ComputeShapeMembership = ComputeShapeMembership, MembershipService = MembershipService });

    internal void SetWorkingBytesSource(Func<long> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var shared = new PreparationWorkingBytes2D(source);
        ArgumentPlan.RegisterWorkingBytesSource(shared);
        _otherWorkingBytes = 0;
        _otherWorkingBytesSource = shared;
    }

    internal void CleanupAfterFailure(IDisposable resource, Exception preparationFailure) => _resources.CleanupAfterFailure(resource, preparationFailure);

    internal RenderPreparationContext2D WithShapeMembership(Pipelines.CoverageMembershipService2D service) => CopyWorkingBudget(new(Device, Target, _resources, ArgumentPlan, _allowReturnedResources)
    {
        ComputeShapeMembership = true,
        MembershipService = service,
        EnforceWorkingBudget = true,
        MaxWorkingBytes = MaxWorkingBytes,
    });

    internal DrawRecordResources2D? RecordResources() => _resources.Records();

    internal SolidRectangleResources2D? RectangleResources() => _resources.Rectangles();

    private RenderPreparationContext2D CopyWorkingBudget(RenderPreparationContext2D target)
    {
        target._otherWorkingBytes = _otherWorkingBytes;
        target._otherWorkingBytesSource = _otherWorkingBytesSource;
        return target;
    }
}
