namespace Lumyte.Graphics.RenderGraph;

internal struct GpuRenderGraphStructureHasher
{
    private const ulong Offset = 14_695_981_039_346_656_037;
    private const ulong Prime = 1_099_511_628_211;
    private ulong value;

    public ulong Value => value == 0 ? Offset : value;

    public void Add(bool item) => Add(item ? 1ul : 0ul);
    public void Add(int item) => Add(unchecked((ulong)item));
    public void Add(uint item) => Add((ulong)item);

    public void Add(ulong item)
    {
        if (value == 0) { value = Offset; }
        for (int index = 0; index < sizeof(ulong); index++)
        {
            value = (value ^ (byte)item) * Prime;
            item >>= 8;
        }
    }

    public void Add(string item)
    {
        Add(item.Length);
        foreach (char character in item)
        {
            value = (value ^ (byte)character) * Prime;
            value = (value ^ (byte)(character >> 8)) * Prime;
        }
    }
}
