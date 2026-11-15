namespace Lumyte.Graphics.Direct3D12;

/// <summary>Native queue and fence boundary, replaceable by deterministic managed fault injection.</summary>
internal interface IDirect3D12QueueDriver
{
    /// <summary>Creates the native completion fence.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="fence">The fence at the native boundary.</param>
    void CreateFence(Direct3D12Device owner, out nint fence);

    /// <summary>Issues one native queue wait.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="fence">The fence at the native boundary.</param>
    /// <param name="value">The value at the native boundary.</param>
    void Wait(Direct3D12Device owner, nint fence, ulong value);

    /// <summary>Issues one native queue signal.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="fence">The fence at the native boundary.</param>
    /// <param name="value">The value at the native boundary.</param>
    void Signal(Direct3D12Device owner, nint fence, ulong value);

    /// <summary>Executes native command lists.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="commands">The commands at the native boundary.</param>
    void Execute(Direct3D12Device owner, ReadOnlySpan<nint> commands);

    /// <summary>Issues a completion marker after preceding native operations.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="fence">The fence at the native boundary.</param>
    void SignalCompletion(Direct3D12Device owner, nint fence);

    /// <summary>Queries the actual native fence value.</summary>
    /// <param name="fence">The fence at the native boundary.</param>
    /// <returns>The native result.</returns>
    ulong Completed(nint fence);

    /// <summary>Queries native device removal.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <returns>The native result.</returns>
    int DeviceRemoved(Direct3D12Device owner);

    /// <summary>Registers a non-cancelable native completion observation.</summary>
    /// <param name="owner">The owner at the native boundary.</param>
    /// <param name="fence">The fence at the native boundary.</param>
    /// <returns>The native result.</returns>
    Task WaitForCompletion(Direct3D12Device owner, nint fence);

    /// <summary>Releases the native fence reference.</summary>
    /// <param name="fence">The fence at the native boundary.</param>
    void Release(nint fence);
}
