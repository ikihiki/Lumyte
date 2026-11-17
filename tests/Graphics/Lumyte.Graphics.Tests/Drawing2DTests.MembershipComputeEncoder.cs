using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

/// <summary>Counts successful encoded dispatch calls, not scene estimates or compute passes.</summary>
public sealed partial class Drawing2DTests
{
    private sealed class MembershipCountingComputeEncoder(IComputeEncoder native, Action dispatched) : IComputeEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments => native.SetArguments(in arguments);

        public void SetArguments<T>(in T arguments, string rootParameter, string? shaderTypeName)
            where T : unmanaged => native.SetArguments(in arguments, rootParameter, shaderTypeName);

        public void SetPipeline(IGraphicsComputePipeline pipeline) => native.SetPipeline(pipeline);

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            native.Dispatch(groupCountX, groupCountY, groupCountZ);
            dispatched();
        }

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
        {
            native.DispatchIndirect(arguments);
            dispatched();
        }

        public void End() => native.End();
    }
}
