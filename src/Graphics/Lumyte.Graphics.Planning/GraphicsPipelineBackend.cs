using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;

/// <summary>Connects typed Core stages directly to one common frame declaration, without nested GPU preparation.</summary>
/// <remarks>Initializes a backend for one explicit frame.</remarks>
/// <param name = "frame">The common declaration owner.</param>
public sealed class GraphicsPipelineBackend(GraphicsFrameScope frame) : IPipelineBackend
{
    private readonly GraphicsFrameScope _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    private readonly Dictionary<int, GraphicsStageRequirement?> _reservations = [];

    /// <inheritdoc/>
    public void Validate(int node, StageRequirements requirements)
    {
        _frame.Plan.Mutable();
        GraphicsStageRequirement? graphics = Requirement(requirements);
        if (graphics is not null && (graphics.MaximumPasses < 0 || graphics.MaximumResources < 0 || graphics.MaximumPasses > _frame.Session.Budget.MaximumPasses || graphics.MaximumResources > _frame.Session.Budget.MaximumResources))
        {
            throw new InvalidOperationException("Graphics stage reservation exceeds the finite frame budget.");
        }

        if (_reservations.ContainsKey(node))
        {
            throw new InvalidOperationException("A graphics backend instance is bound to exactly one pipeline build; create another explicit backend for another build.");
        }

        _reservations.Add(node, graphics);
    }

    /// <inheritdoc/>
    public IStageCapabilities BuildCapabilities(int node) => _reservations[node] is null ? new CpuCapabilities() : new GraphicsBuildCapabilities();

    /// <inheritdoc/>
    public IStageCapabilities PrepareCapabilities(int node, StageRequirements compiled, StageRequirements actual)
    {
        _frame.Plan.Mutable();
        GraphicsStageRequirement? fixedRequirement = Requirement(compiled);
        GraphicsStageRequirement? current = Requirement(actual);
        if (fixedRequirement is null && current is null)
        {
            return new CpuCapabilities();
        }

        if (fixedRequirement is null || current is null || current.MaximumPasses > fixedRequirement.MaximumPasses || current.MaximumResources > fixedRequirement.MaximumResources || current.MaximumPasses < 0 || current.MaximumResources < 0)
        {
            throw new InvalidOperationException("Actual graphics requirements exceed the compiled reservation.");
        }

        return new GraphicsStageCapabilities(_frame, current);
    }

    private static GraphicsStageRequirement? Requirement(StageRequirements requirements)
    {
        if (requirements.Extensions.Count == 0)
        {
            return null;
        }

        if (requirements.Extensions.Count != 1 || requirements.Extensions[0] is not GraphicsStageRequirement graphics)
        {
            throw new NotSupportedException("Unsupported graphics pipeline extension or epoch boundary.");
        }

        return graphics;
    }

    private sealed class CpuCapabilities : IStageCapabilities, IStageCpuPolicy
    {
        /// <summary>Gets a value indicating whether gets can read cpu.</summary>
        public bool CanReadCpu => true;

        /// <summary>Gets a value indicating whether gets can publish cpu.</summary>
        public bool CanPublishCpu => true;
    }

    private sealed class GraphicsBuildCapabilities : IStageCapabilities;
}
