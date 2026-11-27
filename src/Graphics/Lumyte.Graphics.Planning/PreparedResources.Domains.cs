using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed partial class PreparedResources
{
    private readonly Dictionary<ArgumentTableReservation, PlanningShaderResources> _resourceSets = [];
    private readonly Dictionary<ResourceIdentity, PlanningShaderResources> _shaderDataDomains = [];
    private int _barrierCount;

    internal GraphicsPlanningBudget Budget { get; init; } = new();

    internal CleanupLedger Owners { get; init; } = new();

    internal PlanPass? CurrentPass { get; set; }

    internal List<IPreparedTransfer> Transfers { get; } = [];

    internal List<(PlanPass Pass, PlanResourceUse Use)> PhysicalTextureUses { get; } = [];

    internal List<(PlanPass Pass, PreparedPlanBarrier Barrier)> PhysicalBufferBarriers { get; } = [];

    internal List<PreparedOperation> Operations { get; } = [];

    internal void ReserveBarrier()
    {
        if (_barrierCount >= Budget.MaximumBarriers)
        {
            throw new InvalidOperationException("Native barrier reservation exceeded during preparation.");
        }

        _barrierCount++;
    }

    internal ArgumentTableReservation Reserve(ArgumentTablePlan tables, uint colors, bool graphics, bool bindsTable = true)
    {
        ShaderResources native = tables.CreateShaderResources();
        tables.ConfigureExecution(native, colors, graphics);
        var resources = new PlanningShaderResources(native);
        ArgumentTableReservation reservation = tables.Reservations[^1];
        _resourceSets.Add(reservation, resources);
        Operations.Add(new(OperationCount, CurrentPass ?? throw new InvalidOperationException("Preparation has no declared pass."), reservation, resources, bindsTable, graphics));
        return reservation;
    }

    internal void JoinShaderData(ResourceIdentity identity, ArgumentTableReservation reservation)
    {
        PlanningShaderResources resources = _resourceSets[reservation];
        if (_shaderDataDomains.TryGetValue(identity, out PlanningShaderResources? domain))
        {
            resources.ShareDomain(domain);
        }
        else
        {
            _shaderDataDomains.Add(identity, resources);
        }
    }
}
