namespace Lumyte.Graphics;

/// <summary>
/// A fixed-size table of logical shader resources. Native descriptor tables and bind
/// groups are created by the active backend and are never exposed through this API.
/// </summary>
public sealed class GpuResourceTable
{
    private readonly TextureId[] textures;
    private readonly SamplerId[] samplers;

    public GpuResourceTable(int textureSlotCount, int samplerSlotCount)
    {
        if (textureSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(textureSlotCount)); }
        if (samplerSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(samplerSlotCount)); }
        if (textureSlotCount == 0 && samplerSlotCount == 0)
        {
            throw new ArgumentException("A resource table must contain at least one slot.");
        }

        textures = new TextureId[textureSlotCount];
        samplers = new SamplerId[samplerSlotCount];
    }

    public int TextureSlotCount => textures.Length;
    public int SamplerSlotCount => samplers.Length;

    /// <summary>Changes only when a slot's logical resource changes.</summary>
    public ulong Revision { get; private set; }

    public TextureId GetTexture(int slot) => textures[slot];
    public SamplerId GetSampler(int slot) => samplers[slot];

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
}
