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
    private readonly TextureId[] storageTextures;
    private readonly BufferId[] writableBuffers;

    public GpuResourceTable(
        int textureSlotCount,
        int samplerSlotCount,
        int bufferSlotCount = 0,
        int storageTextureSlotCount = 0,
        int writableBufferSlotCount = 0)
    {
        if (textureSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(textureSlotCount)); }
        if (samplerSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(samplerSlotCount)); }
        if (bufferSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(bufferSlotCount)); }
        if (storageTextureSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(storageTextureSlotCount)); }
        if (writableBufferSlotCount < 0) { throw new ArgumentOutOfRangeException(nameof(writableBufferSlotCount)); }
        if (textureSlotCount == 0 && samplerSlotCount == 0 && bufferSlotCount == 0
            && storageTextureSlotCount == 0 && writableBufferSlotCount == 0)
        {
            throw new ArgumentException("A resource table must contain at least one descriptor index.");
        }

        textures = new TextureId[textureSlotCount];
        samplers = new SamplerId[samplerSlotCount];
        buffers = new BufferId[bufferSlotCount];
        storageTextures = new TextureId[storageTextureSlotCount];
        writableBuffers = new BufferId[writableBufferSlotCount];
    }

    public int TextureSlotCount => textures.Length;
    public int SamplerSlotCount => samplers.Length;
    public int BufferSlotCount => buffers.Length;
    public int StorageTextureSlotCount => storageTextures.Length;
    public int WritableBufferSlotCount => writableBuffers.Length;

    /// <summary>Changes only when a slot's logical resource changes.</summary>
    public ulong Revision { get; private set; }

    public TextureId GetTexture(int slot) => textures[slot];
    public SamplerId GetSampler(int slot) => samplers[slot];
    public BufferId GetBuffer(int slot) => buffers[slot];
    public TextureId GetStorageTexture(int slot) => storageTextures[slot];
    public BufferId GetWritableBuffer(int slot) => writableBuffers[slot];

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

    public void SetStorageTexture(int slot, TextureId texture)
    {
        if (texture.IsNull) { throw new ArgumentException("Texture ID cannot be null.", nameof(texture)); }
        if (storageTextures[slot] == texture) { return; }
        storageTextures[slot] = texture;
        Revision++;
    }

    public void SetWritableBuffer(int slot, BufferId buffer)
    {
        if (buffer.IsNull) { throw new ArgumentException("Buffer ID cannot be null.", nameof(buffer)); }
        if (writableBuffers[slot] == buffer) { return; }
        writableBuffers[slot] = buffer;
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

    public void ClearStorageTexture(int slot)
    {
        if (storageTextures[slot].IsNull) { return; }
        storageTextures[slot] = default;
        Revision++;
    }

    public void ClearWritableBuffer(int slot)
    {
        if (writableBuffers[slot].IsNull) { return; }
        writableBuffers[slot] = default;
        Revision++;
    }
}
