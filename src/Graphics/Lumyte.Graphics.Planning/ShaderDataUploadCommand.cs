using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ShaderDataUploadCommand<T>(PlanShaderData<T> staging, PlanShaderData<T> target, PlanRange targetRange, IReadOnlyList<IArgumentPiece>[] captured) : PlanCommand
    where T : struct, IShaderArguments
{
    private ShaderDataSlice<T> _source;
    private ShaderDataSlice<T> _destination;

    internal override ulong ArgumentBytes => checked((ulong)captured.Length * (ulong)T.TransferSize);

    internal override int ReferenceCount => checked(captured.Length * T.ReferenceCount);

    internal override IReadOnlyList<PlanResource> PhysicalResources
        => captured.SelectMany(v => v).SelectMany(p => p.Dependencies).SelectMany(u => new[] { u.Resource }.Concat(ShaderDataClosure.Collect(u.Resource, target.Owner.Frame.Session.Budget).Select(v => v.Resource))).Distinct().ToArray();

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables)
    {
        _source = resources.ShaderData(staging, staging.Range);
        _destination = resources.ShaderData(target, targetRange);
        if (_source.Buffer.ShaderElementStrideInBytes != _destination.Buffer.ShaderElementStrideInBytes)
        {
            throw new ArgumentException("Shader-data upload source and destination layouts differ.");
        }

        resources.ReserveBarrier();
        resources.PhysicalBufferBarriers.Add((resources.CurrentPass!, new PreparedShaderDataBarrier<T>(new() { Buffer = _source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) })));
        ArgumentTableReservation reservation = resources.Reserve(tables, 0, graphics: false, bindsTable: false);
        int operation = resources.OperationCount++;
        reservation.InspectResources((allocation, access) => resources.PhysicalBindings.Add(new(operation, allocation, access)));
        resources.JoinShaderData(target.Identity, reservation);
        foreach (IArgumentPiece piece in captured.SelectMany(v => v))
        {
            piece.Prepare(reservation, resources);
        }

        var transfer = new PreparedShaderDataUpload<T>(_source.Buffer, captured);
        resources.Transfers.Add(transfer);
        resources.Owners.Add(transfer);
    }

    internal override void Record(IGraphicsCommandBuffer commands) => commands.CopyBuffer(_source, _destination);
}
