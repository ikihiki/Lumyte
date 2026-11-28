using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class IndirectComputeCommand<TArguments>(ComputeKernel<TArguments> kernel, IReadOnlyList<IArgumentPiece> pieces, PlanBuffer<DispatchIndirectArguments> buffer, PlanRange range) : PlanCommand
    where TArguments : struct, IShaderArguments
{
    private readonly ComputePassDesc _description = new();
    private readonly bool _hasReferences = pieces.Any(p => p.HasReference);
    private PreparedArguments<TArguments> _arguments;
    private ArgumentTableReservation? _reservation;
    private BufferSlice<DispatchIndirectArguments> _slice;

    internal override IReadOnlyList<PlanResource> PhysicalResources => pieces.SelectMany(p => p.Dependencies).Select(u => u.Resource).Distinct().ToArray();

    internal override ulong ArgumentBytes => checked((ulong)TArguments.TransferSize);

    internal override int ReferenceCount => TArguments.ReferenceCount;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables)
    {
        _slice = resources.Buffer(buffer, range);
        _reservation = resources.Reserve(tables, 0, graphics: false);
        int ordinal = resources.OperationCount++;
        _reservation.InspectResources((allocation, access) => resources.PhysicalBindings.Add(new(ordinal, allocation, access)));
        foreach (IArgumentPiece piece in pieces)
        {
            piece.Prepare(_reservation, resources);
        }
    }

    internal override void PrepareNative(IGraphicsCommandBuffer commands)
    {
        _arguments = new(pieces);
        commands.PrepareCompute(kernel.Pipeline, _hasReferences ? _reservation!.Table : null, _arguments);
    }

    internal override void Record(IGraphicsCommandBuffer commands)
    {
        IComputeEncoder encoder = commands.BeginComputePass(_description);
        try
        {
            encoder.SetPipeline(kernel.Pipeline);
            if (_hasReferences)
            {
                encoder.SetArgumentTable(_reservation!.Table);
            }

            encoder.SetArguments(_arguments);

            encoder.DispatchIndirect(_slice);
        }
        finally
        {
            encoder.End();
        }
    }
}
