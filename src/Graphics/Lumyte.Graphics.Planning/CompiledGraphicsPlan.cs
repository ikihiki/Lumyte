using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares compiled graphics plan.</summary>
public sealed class CompiledGraphicsPlan : IDisposable
{
    private readonly GraphicsFrameScope _frame;
    private readonly PlanResource[] _exports;
    private bool _disposed;
    private bool _prepared;
    private PreparedGraphicsExecution? _preparation;

    internal CompiledGraphicsPlan(GraphicsFrameScope frame, PlanPass[] passes, PlanBarrier[] barriers, PlanResource[] exports, PlanTextureExport[] textureExports, PlanResourceExport[] resourceExports)
    {
        _frame = frame;
        _exports = exports;
        Passes = Array.AsReadOnly(passes);
        Barriers = Array.AsReadOnly(barriers);
        TextureExports = Array.AsReadOnly(textureExports);
        ResourceExports = Array.AsReadOnly(resourceExports);
        frame.Retain();
    }

    /// <summary>Gets passes.</summary>
    public IReadOnlyList<PlanPass> Passes { get; }

    /// <summary>Gets barriers.</summary>
    public IReadOnlyList<PlanBarrier> Barriers { get; }

    /// <summary>Gets explicit final texture release states, applied after the last recorded operation.</summary>
    public IReadOnlyList<PlanTextureExport> TextureExports { get; }

    /// <summary>Gets buffer and shader-data final external access dependencies.</summary>
    public IReadOnlyList<PlanResourceExport> ResourceExports { get; }

    /// <summary>Declares retry cleanup async.</summary>
    /// <returns>The declared result.</returns>
    public ValueTask RetryCleanupAsync() => _frame.Session.Execution.RunAsync(() => _preparation?.Dispose(), retiring: true);

    /// <summary>Declares prepare async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public async ValueTask<PreparedGraphicsExecution> PrepareAsync(CancellationToken cancellationToken = default)
    {
        PreparedGraphicsExecution? result = null;
        try
        {
            await _frame.Session.Execution.RunAsync(() => result = PrepareCore(cancellationToken), cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (IPreparedTransfer transfer in result!.Resources.Transfers)
            {
                ValueTask mapping = default;
                await _frame.Session.Execution.RunAsync(() => mapping = transfer.MapAsync(), cancellationToken: cancellationToken).ConfigureAwait(false);
                await mapping.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            await _frame.Session.Execution.RunAsync(
                () =>
                {
                    foreach (IPreparedTransfer transfer in result.Resources.Transfers)
                    {
                        transfer.Complete();
                    }

                    foreach (PlanCommand command in Passes.SelectMany(pass => pass.Commands))
                    {
                        command.PrepareNative(result.Commands!);
                    }

                    result.Ready = true;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception primary)
        {
            if (result is not null)
            {
                try
                {
                    await _frame.Session.Execution.RunAsync(result.Dispose, retiring: true).ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(primary, cleanup);
                }
            }

            throw;
        }
    }

    /// <summary>Declares dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_preparation is { IsDisposed: false })
        {
            throw new InvalidOperationException("Prepared owners remain live.");
        }

        _disposed = true;
        _frame.Release();
    }

    internal bool IsExported(PlanResource resource) => _exports.Contains(resource);

    private PreparedGraphicsExecution PrepareCore(CancellationToken cancellationToken)
    {
        _frame.Session.Execution.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_prepared)
        {
            throw new InvalidOperationException("A compiled plan prepares once.");
        }

        _prepared = true;
        var owners = new CleanupLedger();
        var resources = new PreparedResources { Owners = owners, Budget = _frame.Session.Budget };
        _preparation = new(_frame.Session, this, owners, resources);
        try
        {
            PlanResource[] used = Passes.SelectMany(p => p.Uses.Select(u => u.Resource)).Concat(Passes.SelectMany(p => p.Commands).SelectMany(c => c.PhysicalResources)).Concat(_exports).Distinct().ToArray();
            ResourceIdentity[] identities = used.Select(r => r.Identity).Distinct().ToArray();
            if (Passes.Count > _frame.Session.Budget.MaximumPasses || identities.Length > _frame.Session.Budget.MaximumResources)
            {
                throw new InvalidOperationException("Plan budget exceeded before allocation.");
            }

            ulong allocationBytes = 0;
            foreach (ResourceIdentity identity in identities)
            {
                if (identity.SurfaceFrame is { Status: not SurfaceFrameStatus.Acquired })
                {
                    throw new InvalidOperationException("The acquired image was released before native preparation.");
                }

                allocationBytes = checked(allocationBytes + identity.Measure(_frame.Session.Device));
            }

            ulong argumentBytes = 0;
            int references = 0;
            int commandCount = 0;
            foreach (PlanCommand command in Passes.SelectMany(p => p.Commands))
            {
                argumentBytes = checked(argumentBytes + command.ArgumentBytes);
                references = checked(references + command.ReferenceCount);
                commandCount++;
            }

            if (allocationBytes > _frame.Session.Budget.MaximumAllocationBytes || argumentBytes > _frame.Session.Budget.MaximumArgumentBytes || references > _frame.Session.Budget.MaximumTableReferences || commandCount > _frame.Session.Budget.MaximumCommands)
            {
                throw new InvalidOperationException("Allocation, payload, table or command budget exceeded before native allocation.");
            }

            foreach (ResourceIdentity? identity in identities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IDisposable allocation = identity.Allocate(_frame.Session.Device);
                resources.Allocations.Add(identity, allocation);
                if (identity.Imported is null)
                {
                    owners.Add(allocation);
                }
            }

            foreach (PlanTexture texture in Passes.SelectMany(p => p.Commands).SelectMany(c => c.PhysicalResources).Concat(_exports).OfType<PlanTexture>().Distinct())
            {
                IGraphicsTextureView view = ((IGraphicsTexture)resources.Allocations[texture.Identity]).CreateView(texture.NativeViewDescription);
                owners.Add(view);
                resources.Views.Add(texture, view);
            }

            var tables = new ArgumentTablePlan(_frame.Session.Device);
            owners.Add(tables);
            foreach (PlanPass pass in Passes)
            {
                resources.CurrentPass = pass;
                foreach (PlanCommand command in pass.Commands)
                {
                    command.Prepare(resources, tables);
                }
            }

            tables.Materialize(cancellationToken: cancellationToken, shareTables: false);
            PreparedBindingClosure.Complete(resources);
            _preparation.PreparedBarriers = BarrierPreparation.Prepare(this, resources);
            _preparation.Commands = _frame.Session.Device.CreateCommandBuffer(new());
            resources.CurrentPass = null;
            return _preparation;
        }
        catch (Exception primary)
        {
            try
            {
                _preparation.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }

            throw;
        }
    }
}
