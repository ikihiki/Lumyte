using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsStageCapabilities(GraphicsStageBuildContext build, GraphicsRun? run, int node, GraphicsStageRequirements? requirements) : IGraphicsStageCapabilities, IStageScopeBinding, IStageRequirementValidation, IStageCpuPolicy
{
    private readonly GraphicsStageRequirements? _compiled = requirements;
    private GraphicsStagePreparationContext? _preparation;
    private GraphicsStageRequirements? _effective = requirements;

    public GraphicsStageBuildContext Build { get; } = build;

    public GraphicsStagePreparationContext Preparation => _preparation ?? throw new InvalidOperationException("Preparation capability is only available during an invocation.");

    public bool CanReadCpu => _effective is null || _effective.Domain == GraphicsExecutionDomain.Upload;

    public bool CanPublishCpu => _effective is null;

    public void Validate(StageRequirements actual)
    {
        var candidate = actual.Extensions.SingleOrDefault() as GraphicsStageRequirements;
        if (actual.Extensions.Count > 1 || (actual.Extensions.Count == 1 && candidate is null) ||
            (_compiled is null) != (candidate is null) ||
            (_compiled is not null && candidate is not null &&
            (candidate.Domain != _compiled.Domain || candidate.MaxDeviceBytes > _compiled.MaxDeviceBytes ||
            !candidate.Artifacts.SequenceEqual(_compiled.Artifacts) || !candidate.InputAccesses.SequenceEqual(_compiled.InputAccesses))))
        {
            throw new InvalidOperationException("Actual stage _compiled differ from the compiled capabilities or reservations.");
        }

        run?.ReserveScratch(actual.MaxHostBytes);
        _effective = candidate;
    }

    public void Complete(bool succeeded)
    {
        if (succeeded)
        {
            run?.Prepared(node);
        }
    }

    public void Bind(StagePreparationContext context)
    {
        if (run is null)
        {
            throw new InvalidOperationException("Build capabilities cannot bind a preparation scope.");
        }

        for (int index = 0; index < context.Inputs.Count; index++)
        {
            if (((IValueLeaseMetadata)context.Inputs[index]).UntypedValue is IGraphicsDeviceValue placement)
            {
                DeviceValue value = placement.Value ?? throw new InvalidOperationException("An input contains an uninitialized device placement.");
                if (!ReferenceEquals(value.Run, run))
                {
                    throw new InvalidOperationException("Device input belongs to another preparation.");
                }

                ResourceAccess access = _effective?.InputAccesses.FirstOrDefault(candidate => candidate.InputIndex == index)?.Access ?? ResourceAccess.None;
                value.ValidateAccess(access, node);
            }
        }

        _preparation = new GraphicsStagePreparationContext(run, context, node, _effective, Build);
    }
}
