using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Collects shader resources for one indivisible draw or dispatch without exposing native binding ownership.</summary>
/// <remarks>Collect, prepare, and record serially. Registered resources remain borrowed.</remarks>
public class ShaderResources
{
    private readonly ArgumentTableReservation _reservation;

    internal ShaderResources(ArgumentTableReservation reservation)
    {
        _reservation = reservation;
    }

    /// <summary>Initializes a new instance of the <see cref="ShaderResources"/> class sharing the same resource set.</summary>
    /// <param name="resources">The opaque resource set to wrap, not a sibling allocation.</param>
    protected ShaderResources(ShaderResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _reservation = resources._reservation;
        _reservation.Owner.ValidateScope(_reservation);
    }

    /// <summary>Gets a value indicating whether this set's references can be read, including during preparation callbacks.</summary>
    public bool IsReady => _reservation.IsMaterialized;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ShaderResources other && ReferenceEquals(_reservation, other._reservation);

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(_reservation);

    /// <summary>Creates an independent resource set with the same owner.</summary>
    /// <returns>An empty sibling for another indivisible draw or dispatch.</returns>
    public virtual ShaderResources CreateSibling()
    {
        _reservation.ValidateCollecting();
        return _reservation.Owner.CreateShaderResources();
    }

    /// <summary>Registers an exact borrowed sampled view.</summary>
    /// <param name="view">The immutable sampled view.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(IGraphicsTextureView view) => _reservation.RegisterTexture(view);

    /// <summary>Registers a sampled view resolved during preparation.</summary>
    /// <param name="resolve">The owner-scoped view resolver.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(Func<IGraphicsTextureView> resolve) => _reservation.RegisterTexture(resolve);

    /// <summary>Registers an immutable borrowed sampler by identity.</summary>
    /// <param name="sampler">The sampled resource's sampler.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<IGraphicsSampler> RegisterSampler(IGraphicsSampler sampler) => _reservation.RegisterSampler(sampler);

    /// <summary>Registers an exact borrowed raw buffer range and its shader access.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="range">The borrowed buffer range.</param>
    /// <param name="access">The actual shader accesses.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(BufferSlice<T> range, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged => _reservation.RegisterBuffer(range, access);

    /// <summary>Registers a raw buffer range resolved during preparation.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="resolve">The final buffer range resolver.</param>
    /// <param name="access">The actual shader accesses.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(Func<BufferSlice<T>> resolve, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged => _reservation.RegisterBuffer(resolve, access);

    /// <summary>Registers an exact borrowed shader-data range.</summary>
    /// <typeparam name="T">The logical shader element type.</typeparam>
    /// <param name="range">The device-memory shader-data range.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(ShaderDataSlice<T> range)
        where T : struct, IShaderData => _reservation.RegisterBuffer(range);

    /// <summary>Registers a shader-data range resolved during preparation.</summary>
    /// <typeparam name="T">The logical shader element type.</typeparam>
    /// <param name="resolve">The final shader-data range resolver.</param>
    /// <returns>A pending resource reference.</returns>
    public ArgumentRegistration<T> RegisterShaderData<T>(Func<ShaderDataSlice<T>> resolve)
        where T : struct, IShaderData => _reservation.RegisterShaderData(resolve);

    /// <summary>Adds a resource identity and shader-access validation before native allocation.</summary>
    /// <param name="inspect">The resource declaration validator.</param>
    public void InspectResources(Action<object, ArgumentBufferAccess> inspect) => _reservation.InspectResources(inspect);

    /// <summary>Adds a synchronous preparation callback after all live references become readable.</summary>
    /// <param name="finalize">The callback; recording remains prohibited until every live callback succeeds.</param>
    public void WhenReady(Action finalize) => _reservation.WhenReady(finalize);

    /// <summary>Retains this generation's native ownership through GPU completion.</summary>
    /// <returns>An independently retryable, idempotent lifetime lease.</returns>
    public IDisposable Retain()
    {
        _reservation.Owner.ValidateScope(_reservation);
        return _reservation.Owner.Retain();
    }

    /// <summary>Discards only this unpublished resource set without changing sibling sets.</summary>
    public void Cancel()
    {
        _reservation.ValidateCollecting();
        _reservation.Owner.Discard([_reservation]);
    }

    /// <summary>Selects this set's native bindings and root arguments, then records a draw.</summary>
    /// <typeparam name="T">The generated root argument type.</typeparam>
    /// <param name="encoder">The render encoder with a selected pipeline and render state.</param>
    /// <param name="arguments">The root argument values.</param>
    /// <param name="vertexCount">The vertex count.</param>
    /// <param name="instanceCount">The instance count.</param>
    /// <param name="firstVertex">The first vertex.</param>
    /// <param name="firstInstance">The first instance.</param>
    public void Draw<T>(IRenderEncoder encoder, in T arguments, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(encoder);
        IArgumentTable? table = _reservation.RecordingTable(graphics: true);
        if (table is not null)
        {
            encoder.SetArgumentTable(table);
        }

        encoder.SetArguments(in arguments);
        encoder.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Selects this set's native bindings and root arguments, then records an indexed draw.</summary>
    /// <typeparam name="T">The generated root argument type.</typeparam>
    /// <param name="encoder">The render encoder with a selected pipeline, state, and index buffer.</param>
    /// <param name="arguments">The root argument values.</param>
    /// <param name="indexCount">The index count.</param>
    /// <param name="instanceCount">The instance count.</param>
    /// <param name="firstIndex">The first index relative to the selected range.</param>
    /// <param name="baseVertex">The signed vertex offset.</param>
    /// <param name="firstInstance">The first instance.</param>
    public void DrawIndexed<T>(IRenderEncoder encoder, in T arguments, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(encoder);
        IArgumentTable? table = _reservation.RecordingTable(graphics: true);
        if (table is not null)
        {
            encoder.SetArgumentTable(table);
        }

        encoder.SetArguments(in arguments);
        encoder.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Selects this set's native bindings and root arguments, then records compute workgroups.</summary>
    /// <typeparam name="T">The generated root argument type.</typeparam>
    /// <param name="encoder">The compute encoder with a selected pipeline.</param>
    /// <param name="arguments">The root argument values.</param>
    /// <param name="x">The X workgroup count.</param>
    /// <param name="y">The Y workgroup count.</param>
    /// <param name="z">The Z workgroup count.</param>
    public void Dispatch<T>(IComputeEncoder encoder, in T arguments, uint x, uint y = 1, uint z = 1)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(encoder);
        IArgumentTable? table = _reservation.RecordingTable(graphics: false);
        if (table is not null)
        {
            encoder.SetArgumentTable(table);
        }

        encoder.SetArguments(in arguments);
        encoder.Dispatch(x, y, z);
    }

    internal bool Uses(ArgumentTableReservation reservation) => ReferenceEquals(_reservation, reservation);

    /// <summary>Connects a host's immutable collection boundary to every inherited registration path.</summary>
    /// <param name="validate">The serial host collection guard.</param>
    protected void ValidateBeforeCollection(Action validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _reservation.AddCollectionGuard(validate);
    }
}
