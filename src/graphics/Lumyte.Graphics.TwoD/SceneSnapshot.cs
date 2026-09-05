using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>GPU-resident retained scene data that can be updated without replacing unchanged ranges.</summary>
public sealed class SceneSnapshot : IDisposable, IPreparedDrawing
{
    private const ulong NodeStride = 256;

    private readonly Scene scene;
    private readonly Dictionary<int, UploadedNodeVersion> uploaded = [];
    private OwnedBuffer? primitiveBuffer;
    private PreparedBatch[] batches = [];
    private PreparedImage[] images = [];
    private int capacity;
    private bool disposed;

    internal SceneSnapshot(Renderer owner, Scene scene, GpuTextureDescription targetDescription)
    {
        Owner = owner;
        this.scene = scene;
        TargetDescription = targetDescription;
        Update();
    }

    public int CommandCount { get; private set; }
    public bool IsEmpty => CommandCount == 0;
    public GpuTextureDescription TargetDescription { get; }
    public SceneUpdateStatistics LastUpdate { get; private set; }

    internal Renderer Owner { get; }
    internal IReadOnlyList<PreparedBatch> Batches => batches;
    internal IReadOnlyList<PreparedImage> Images => images;
    internal OwnedBuffer? PrimitiveBuffer => RequireAlive(primitiveBuffer);
    internal OwnedBuffer? PolygonBuffer => null;
    internal OwnedBuffer? PathBuffer => null;

    Renderer IPreparedDrawing.Owner => Owner;
    IReadOnlyList<PreparedBatch> IPreparedDrawing.Batches => Batches;
    IReadOnlyList<PreparedImage> IPreparedDrawing.Images => Images;
    OwnedBuffer? IPreparedDrawing.PrimitiveBuffer => PrimitiveBuffer;
    OwnedBuffer? IPreparedDrawing.PolygonBuffer => null;
    OwnedBuffer? IPreparedDrawing.PathBuffer => null;

    public SceneUpdateStatistics Update()
    {
        VerifyAlive();
        SceneNodeState[] nodes = scene.Capture();
        int neededCapacity = Math.Max(scene.SlotCount, 1);
        bool reallocated = primitiveBuffer is null || capacity < neededCapacity;
        if (reallocated)
        {
            int newCapacity = 1;
            while (newCapacity < neededCapacity) { newCapacity = checked(newCapacity * 2); }
            primitiveBuffer?.Dispose();
            primitiveBuffer = OwnedBuffer.Create(Owner.Backend, checked((ulong)newCapacity * NodeStride));
            capacity = newCapacity;
            uploaded.Clear();
        }

        var nextBatches = new List<PreparedBatch>();
        var nextImages = new List<PreparedImage>();
        var imageIndices = new Dictionary<ImageId, int>();
        var liveSlots = new HashSet<int>();
        var targetBounds = new Rect(0, 0, TargetDescription.Width, TargetDescription.Height);
        int updated = 0;
        ulong bytesUploaded = 0;
        int commandCount = 0;
        Span<byte> commandBytes = stackalloc byte[GpuDrawCommand.Size];
        foreach (SceneNodeState node in nodes)
        {
            liveSlots.Add(node.Slot);
            if (!node.Visible || node.Content is not { } content) { continue; }
            RecordedCommand command = content.Resolve(Owner, node.Transform, node.Clip);
            Rect? clip = command.Clip is { } requested
                ? Rect.Intersect(targetBounds, requested)
                : targetBounds;
            if (clip is null
                || Rect.Intersect(clip.Value, command.Bounds.TransformBounds(command.Transform)) is null)
            {
                continue;
            }

            var version = new UploadedNodeVersion(node.Generation, node.Revision);
            if (!uploaded.TryGetValue(node.Slot, out UploadedNodeVersion previous) || previous != version)
            {
                GpuDrawCommand gpuCommand = Renderer.CreateGpuCommand(command, TargetDescription);
                MemoryMarshal.Write(commandBytes, in gpuCommand);
                primitiveBuffer!.Write(checked((ulong)node.Slot * NodeStride), commandBytes);
                uploaded[node.Slot] = version;
                updated++;
                bytesUploaded += GpuDrawCommand.Size;
            }

            PreparedBatchKind kind = command.Kind switch
            {
                DrawCommandKind.Image => PreparedBatchKind.Image,
                DrawCommandKind.DistanceField => PreparedBatchKind.DistanceField,
                _ => PreparedBatchKind.Primitive,
            };
            int imageIndex = -1;
            if (kind is PreparedBatchKind.Image or PreparedBatchKind.DistanceField)
            {
                if (!imageIndices.TryGetValue(command.Image, out imageIndex))
                {
                    RegisteredImage image = Owner.RequireImage(command.Image);
                    imageIndex = nextImages.Count;
                    nextImages.Add(new(command.Image, image.Texture, image.Description, image.Sampler));
                    imageIndices.Add(command.Image, imageIndex);
                }
            }
            nextBatches.Add(new(
                kind,
                checked((ulong)node.Slot * NodeStride),
                GpuDrawCommand.Size,
                1,
                clip.Value,
                imageIndex));
            commandCount++;
        }

        foreach (int stale in uploaded.Keys.Where(slot => !liveSlots.Contains(slot)).ToArray())
        {
            uploaded.Remove(stale);
        }
        batches = nextBatches.ToArray();
        images = nextImages.ToArray();
        CommandCount = commandCount;
        LastUpdate = new(updated, bytesUploaded, reallocated);
        return LastUpdate;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        primitiveBuffer?.Dispose();
        primitiveBuffer = null;
        batches = [];
        images = [];
        disposed = true;
    }

    internal void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);

    void IPreparedDrawing.VerifyAlive() => VerifyAlive();

    private T? RequireAlive<T>(T? value) where T : class
    {
        VerifyAlive();
        return value;
    }
}
