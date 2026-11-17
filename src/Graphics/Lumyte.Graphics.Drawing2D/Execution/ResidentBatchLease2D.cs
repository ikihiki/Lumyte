using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// A lease keeps the complete compiled owner (including its encoded argument-table
// generation) alive. The cache gate serializes lease counts and recording. Forks
// require a completed immutable payload or a same-frame initialization alias;
// alias recording separately proves its successful earlier command producer.
internal sealed class ResidentBatchLease2D : IRetainedCommandBatch2D, IRetainedMemory2D, IPreparedWorkingBudget2D
{
    private readonly Owner _owner;
    private bool _released;

    internal ResidentBatchLease2D(RetainedBatchRun2D batch)
    {
        _owner = new(batch);
    }

    private ResidentBatchLease2D(Owner owner)
    {
        _owner = owner;
        checked
        {
            owner.Users++;
        }
    }

    public bool HasStableOwners => _owner.Batch.HasStableOwners;

    public bool EnforcesWorkingBudget => _owner.Batch.EnforcesWorkingBudget;

    public long EstimatedBytes => checked(_owner.Batch.EstimatedBytes + 192);

    public void AccountMemory(Dictionary<object, long> owners)
    {
        owners.TryAdd(this, 128);
        owners.TryAdd(_owner, 64);
        _owner.Batch.AccountMemory(owners);
    }

    public void DeclareResources(IDrawingResourceDeclarations2D resources) => _owner.Batch.DeclareResources(resources);

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => RecordRetained(commands, target, false);

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        => RecordInstance(commands, target, resident, default);

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        if (_owner.Users == 1)
        {
            // Preserve the last reference and retry ownership if cleanup throws.
            _owner.Batch.Dispose();
        }

        _owner.Users--;
        _released = true;
    }

    internal ResidentBatchLease2D Fork()
    {
        ObjectDisposedException.ThrowIf(_released, this);
        return new(_owner);
    }

    internal void RecordInstance(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident, Vector2 translation, bool earlierProducer = false)
    {
        ObjectDisposedException.ThrowIf(_released, this);
        _owner.Batch.RecordInstance(commands, target, resident, translation, earlierProducer);
    }

    private sealed class Owner(RetainedBatchRun2D batch)
    {
        internal RetainedBatchRun2D Batch { get; } = batch;

        internal int Users { get; set; } = 1;
    }
}
