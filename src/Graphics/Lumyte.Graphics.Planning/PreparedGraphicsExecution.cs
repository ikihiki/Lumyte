using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares prepared graphics execution.</summary>
public sealed class PreparedGraphicsExecution : IDisposable
{
    private readonly CleanupLedger _owners;
    private bool _claimed;
    private bool _everClaimed;
    private int _exportCount;
    private bool _retired;

    internal PreparedGraphicsExecution(GraphicsDeviceSession session, CompiledGraphicsPlan compiled, CleanupLedger owners, PreparedResources resources)
    {
        (Session, Compiled, _owners, Resources) = (session, compiled, owners, resources);
    }

    /// <summary>Gets every descriptor physically exposed by the non-shared operation tables; owners remain pinned until retirement.</summary>
    public IReadOnlyList<PhysicalBindingUse> PhysicalBindings => Resources.PhysicalBindings.AsReadOnly();

    internal bool ContentsReady { get; private set; }

    internal GraphicsDeviceSession Session { get; }

    internal CompiledGraphicsPlan Compiled { get; }

    internal PreparedResources Resources { get; }

    internal IReadOnlyDictionary<PlanPass, IReadOnlyList<PreparedPlanBarrier>> PreparedBarriers { get; set; } = new Dictionary<PlanPass, IReadOnlyList<PreparedPlanBarrier>>();

    internal IGraphicsCommandBuffer? Commands { get; set; }

    internal bool Ready { get; set; }

    internal bool IsDisposed { get; private set; }

    /// <summary>Declares export buffer.</summary>
    /// <param name = "resource">The resource.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public GraphicsExportLease<IGraphicsBuffer<T>> ExportBuffer<T>(PlanBuffer<T> resource)
        where T : unmanaged
    {
        if (IsDisposed || !Ready || !Compiled.IsExported(resource))
        {
            throw new InvalidOperationException("Only declared live exports may be retained.");
        }

        return new(this, (IGraphicsBuffer<T>)Resources.Allocations[resource.Identity]);
    }

    /// <summary>Declares export texture.</summary>
    /// <param name = "resource">The resource.</param>
    /// <returns>The declared result.</returns>
    public GraphicsExportLease<IGraphicsTextureView> ExportTexture(PlanTexture resource)
    {
        if (IsDisposed || !Ready || !Compiled.IsExported(resource))
        {
            throw new InvalidOperationException("Only declared live exports may be retained.");
        }

        return new(this, Resources.Texture(resource));
    }

    /// <summary>Retains an exported typed shader-data allocation together with its entire patched reference domain.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="resource">The exact declared exported version.</param>
    /// <returns>An independent native use lease gated on successful command retirement.</returns>
    public GraphicsExportLease<IGraphicsShaderDataBuffer<T>> ExportShaderData<T>(PlanShaderData<T> resource)
        where T : struct, IShaderArguments
    {
        if (IsDisposed || !Ready || !Compiled.IsExported(resource))
        {
            throw new InvalidOperationException("Only declared live shader-data exports may be retained.");
        }

        return new(this, (IGraphicsShaderDataBuffer<T>)Resources.Allocations[resource.Identity]);
    }

    /// <summary>Declares retire async.</summary>
    /// <returns>The declared result.</returns>
    public ValueTask RetireAsync() => Session.Execution.RunAsync(Dispose, retiring: true);

    /// <summary>Declares dispose.</summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_claimed)
        {
            throw new InvalidOperationException("An execution owns this preparation.");
        }

        _retired = true;
        if (_exportCount != 0)
        {
            return;
        }

        CleanupRetiredOwners();
    }

    internal void RetainExport()
    {
        if (_exportCount >= Session.Budget.MaximumResources)
        {
            throw new InvalidOperationException("Export lease reservation exceeded.");
        }

        _exportCount++;
    }

    internal ValueTask ReleaseExportAsync() => Session.Execution.RunAsync(ReleaseExport, retiring: true);

    internal void ReleaseExport()
    {
        Session.Execution.VerifyAccess();
        if (_exportCount == 1 && _retired)
        {
            CleanupRetiredOwners();
        }

        _exportCount--;
    }

    internal void Claim()
    {
        if (!Ready || IsDisposed || _claimed || _everClaimed || _retired)
        {
            throw new InvalidOperationException("Preparation is unavailable.");
        }

        _claimed = true;
        _everClaimed = true;
    }

    internal void Retire(bool success)
    {
        _claimed = false;
        _retired = true;
        ContentsReady = success;
        Dispose();
    }

    private void CleanupRetiredOwners()
    {
        Session.Execution.VerifyAccess();
        if (Commands is not null)
        {
            Commands.Dispose();
            Commands = null;
        }

        _owners.Cleanup(Session.Budget.CleanupAttempts);
        IsDisposed = true;
    }
}
