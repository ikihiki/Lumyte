namespace Lumyte.Graphics.TwoD;

/// <summary>Owns an R8 atlas and delays physical region reuse until a caller-provided fence completes.</summary>
public sealed class DistanceFieldAtlas : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly OwnedTexture texture;
    private readonly List<AtlasRectangle> freeRectangles = [];
    private readonly Dictionary<int, DistanceFieldEntry> live = [];
    private readonly SortedDictionary<ulong, List<DistanceFieldEntry>> retired = [];
    private readonly Stack<int> reusableSlots = [];
    private readonly Dictionary<int, uint> generations = [];
    private int nextSlot;
    private bool disposed;

    public DistanceFieldAtlas(IGpuBackend backend, uint width = 1024, uint height = 1024)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        Description = new(
            width,
            height,
            GpuFormat.R8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.ColorAttachment);
        texture = OwnedTexture.Create(backend, Description);
        try
        {
            Sampler = backend.CreateSampler(new(
                GpuSamplerFilter.Linear,
                GpuSamplerFilter.Linear,
                GpuSamplerAddressMode.ClampToEdge,
                GpuSamplerAddressMode.ClampToEdge));
        }
        catch
        {
            texture.Dispose();
            throw;
        }
        freeRectangles.Add(new(0, 0, width, height));
    }

    public GpuTextureDescription Description { get; }
    public int LiveRegionCount => live.Count;
    public int PendingRetirementCount => retired.Values.Sum(static entries => entries.Count);

    internal IGpuBackend Backend => backend;
    internal GpuTextureHandle Texture => texture.Texture;
    internal SamplerId Sampler { get; }

    public bool IsAlive(DistanceField field)
    {
        VerifyAlive();
        return ReferenceEquals(field.Owner, this)
            && live.TryGetValue(field.Slot, out DistanceFieldEntry entry)
            && entry.Generation == field.Generation;
    }

    public void Release(DistanceField field, GpuFenceValue afterFence)
    {
        VerifyAlive();
        DistanceFieldEntry entry = Require(field);
        live.Remove(entry.Slot);
        if (!retired.TryGetValue(afterFence.Value, out List<DistanceFieldEntry>? entries))
        {
            entries = [];
            retired.Add(afterFence.Value, entries);
        }
        entries.Add(entry);
    }

    public int Collect(GpuFenceValue completedFence)
    {
        VerifyAlive();
        ulong[] fences = retired.Keys.TakeWhile(value => value <= completedFence.Value).ToArray();
        int count = 0;
        foreach (ulong fence in fences)
        {
            foreach (DistanceFieldEntry entry in retired[fence])
            {
                freeRectangles.Add(entry.Region);
                reusableSlots.Push(entry.Slot);
                count++;
            }
            retired.Remove(fence);
        }
        return count;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        backend.DestroySampler(Sampler);
        texture.Dispose();
        live.Clear();
        retired.Clear();
        freeRectangles.Clear();
        disposed = true;
    }

    internal DistanceField Allocate(
        uint width,
        uint height,
        float distanceRange,
        DistanceFieldEncoding encoding)
    {
        VerifyAlive();
        if (width == 0 || height == 0 || width > Description.Width || height > Description.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        int selected = -1;
        ulong selectedArea = ulong.MaxValue;
        for (int index = 0; index < freeRectangles.Count; index++)
        {
            AtlasRectangle candidate = freeRectangles[index];
            if (candidate.Width >= width && candidate.Height >= height && candidate.Area < selectedArea)
            {
                selected = index;
                selectedArea = candidate.Area;
            }
        }
        if (selected < 0)
        {
            throw new InvalidOperationException("The distance-field atlas has no region large enough for the request.");
        }

        AtlasRectangle free = freeRectangles[selected];
        freeRectangles.RemoveAt(selected);
        var region = new AtlasRectangle(free.X, free.Y, width, height);
        uint rightWidth = free.Width - width;
        uint bottomHeight = free.Height - height;
        if (rightWidth != 0)
        {
            freeRectangles.Add(new(free.X + width, free.Y, rightWidth, height));
        }
        if (bottomHeight != 0)
        {
            freeRectangles.Add(new(free.X, free.Y + height, free.Width, bottomHeight));
        }

        int slot = reusableSlots.TryPop(out int reused) ? reused : nextSlot++;
        uint generation = generations.TryGetValue(slot, out uint previous)
            ? checked(previous + 1)
            : 1;
        generations[slot] = generation;
        live.Add(slot, new(slot, generation, region, distanceRange, encoding));
        return new(this, slot, generation);
    }

    internal DistanceFieldEntry Require(DistanceField field)
    {
        VerifyAlive();
        if (!ReferenceEquals(field.Owner, this)
            || !live.TryGetValue(field.Slot, out DistanceFieldEntry entry)
            || entry.Generation != field.Generation)
        {
            throw new ArgumentException("Distance field is stale or belongs to another atlas.", nameof(field));
        }
        return entry;
    }

    private void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);
}
