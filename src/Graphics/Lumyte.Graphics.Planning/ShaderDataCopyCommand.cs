using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ShaderDataCopyCommand<T>(PlanShaderData<T> source, PlanRange sourceRange, PlanShaderData<T> destination, PlanRange destinationRange) : PlanCommand
    where T : struct, IShaderArguments
{
    private ShaderDataSlice<T> _source;
    private ShaderDataSlice<T> _destination;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables)
    {
        _source = resources.ShaderData(source, sourceRange);
        _destination = resources.ShaderData(destination, destinationRange);
        if (_source.Buffer.IsMapped || _destination.Buffer.IsMapped || _source.Buffer.ShaderElementStrideInBytes != _destination.Buffer.ShaderElementStrideInBytes)
        {
            throw new ArgumentException("Shader-data copy requires unmapped, layout-compatible native storage.");
        }

        if (T.ReferenceCount != 0)
        {
            ArgumentTableReservation reservation = resources.Reserve(tables, 0, graphics: false, bindsTable: false);
            resources.OperationCount++;
            resources.JoinShaderData(source.Identity, reservation);
            resources.JoinShaderData(destination.Identity, reservation);
        }
    }

    internal override void Record(IGraphicsCommandBuffer commands) => commands.CopyBuffer(_source, _destination);
}
