namespace Lumyte.Graphics;

/// <summary>
/// A fixed-size table of logical shader resources. Native descriptor tables and bind
/// groups are created by the active backend and are never exposed through this API.
/// </summary>
public sealed class GpuResourceTable
{
    private readonly TextureId[] textures;
    private readonly SamplerId[] samplers;
    private readonly BufferId[] buffers;

    public GpuResourceTable(int textureSlotCount, int samplerSlotCount, int bufferSlotCount = 0)
    {
        if (textureSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(textureSlotCount)); }
        if (samplerSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(samplerSlotCount)); }
        if (bufferSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(bufferSlotCount)); }
        if (textureSlotCount == 0 && samplerSlotCount == 0 && bufferSlotCount == 0)
        {
            throw new ArgumentException("A resource table must contain at least one descriptor index.");
        }

        textures = new TextureId[textureSlotCount];
        samplers = new SamplerId[samplerSlotCount];
        buffers = new BufferId[bufferSlotCount];
    }

    public int TextureSlotCount => textures.Length;
    public int SamplerSlotCount => samplers.Length;
    public int BufferSlotCount => buffers.Length;

    /// <summary>Changes only when a slot's logical resource changes.</summary>
    public ulong Revision { get; private set; }

    public TextureId GetTexture(int slot) => textures[slot];
    public SamplerId GetSampler(int slot) => samplers[slot];
    public BufferId GetBuffer(int slot) => buffers[slot];

    public void SetTexture(int slot, TextureId texture)
    {
        if (texture.IsNull) { throw new ArgumentException("Texture ID cannot be null.", nameof(texture)); }
        if (textures[slot] == texture) { return; }
        textures[slot] = texture;
        Revision++;
    }

    public void SetSampler(int slot, SamplerId sampler)
    {
        if (sampler.IsNull) { throw new ArgumentException("Sampler ID cannot be null.", nameof(sampler)); }
        if (samplers[slot] == sampler) { return; }
        samplers[slot] = sampler;
        Revision++;
    }

    public void SetBuffer(int slot, BufferId buffer)
    {
        if (buffer.IsNull) { throw new ArgumentException("Buffer ID cannot be null.", nameof(buffer)); }
        if (buffers[slot] == buffer) { return; }
        buffers[slot] = buffer;
        Revision++;
    }

    public void ClearTexture(int slot)
    {
        if (textures[slot].IsNull) { return; }
        textures[slot] = default;
        Revision++;
    }

    public void ClearSampler(int slot)
    {
        if (samplers[slot].IsNull) { return; }
        samplers[slot] = default;
        Revision++;
    }

    public void ClearBuffer(int slot)
    {
        if (buffers[slot].IsNull) { return; }
        buffers[slot] = default;
        Revision++;
    }
}
