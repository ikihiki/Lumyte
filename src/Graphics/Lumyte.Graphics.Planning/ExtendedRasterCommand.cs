using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ExtendedRasterCommand<TArguments, TOperation>(IReadOnlyList<IArgumentPiece> pieces, RasterAttachments attachments, CompleteRasterState state, TOperation operation) : PlanCommand
    where TArguments : struct, IShaderArguments
    where TOperation : IRasterOperation
{
    private readonly bool _hasReferences = pieces.Any(p => p.HasReference);
    private PreparedArguments<TArguments> _arguments;
    private ArgumentTableReservation? _reservation;
    private RenderPassDesc? _description;

    internal override IReadOnlyList<PlanResource> PhysicalResources
        => pieces.SelectMany(p => p.Dependencies).Select(u => u.Resource)
            .Concat(attachments.Colors.Select(c => c.Texture))
            .Concat(attachments.DepthStencil is { } depth ? new[] { depth.Texture } : [])
            .Distinct().ToArray();

    internal override ulong ArgumentBytes => checked((ulong)TArguments.TransferSize);

    internal override int ReferenceCount => TArguments.ReferenceCount;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables)
    {
        _reservation = resources.Reserve(tables, checked((uint)attachments.Colors.Count), graphics: true);
        _description = attachments.Prepare(resources);
        operation.Prepare(resources);
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
        operation.PrepareNative(commands, _description!, state.Raster.RenderState, _hasReferences ? _reservation!.Table : null, _arguments);
    }

    internal override void Record(IGraphicsCommandBuffer commands)
    {
        IRenderEncoder encoder = commands.BeginRenderPass(_description!);
        try
        {
            operation.SelectPipeline(encoder);
            state.Record(encoder);
            operation.BindInputs(encoder);
            if (_hasReferences)
            {
                encoder.SetArgumentTable(_reservation!.Table);
            }

            encoder.SetArguments(_arguments);

            operation.Record(encoder);
        }
        finally
        {
            encoder.End();
        }
    }
}
