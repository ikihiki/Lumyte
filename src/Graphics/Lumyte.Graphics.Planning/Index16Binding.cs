using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class Index16Binding(PlanBuffer<ushort> buffer, PlanRange range) : IIndexBinding
{
    private BufferSlice<ushort> _slice;

    public IndexFormat Format => IndexFormat.Uint16;

    public void Prepare(PreparedResources resources) => _slice = resources.Buffer(buffer, range);

    public void Record(IRenderEncoder encoder) => encoder.SetIndexBuffer(_slice);
}
