using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class Index32Binding(PlanBuffer<uint> buffer, PlanRange range) : IIndexBinding
{
    private BufferSlice<uint> _slice;

    public IndexFormat Format => IndexFormat.Uint32;

    public void Prepare(PreparedResources resources) => _slice = resources.Buffer(buffer, range);

    public void Record(IRenderEncoder encoder) => encoder.SetIndexBuffer(_slice);
}
