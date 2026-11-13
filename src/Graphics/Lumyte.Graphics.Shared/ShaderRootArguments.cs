using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Owns a selected root generation in final native storage, retaining only reference metadata.</summary>
public sealed class ShaderRootArguments : IDisposable
{
    private readonly IShaderArgumentScope? _scope;
    private readonly IReadOnlyList<ShaderValue>? _compatibilityFixups;
    private bool _sealed;
    private bool _disposed;

    private ShaderRootArguments(string rootParameter, string shaderTypeName, int wireSize, IShaderRootStorage storage, IShaderReferenceMetadata references, IShaderArgumentScope? scope, IReadOnlyList<ShaderValue>? compatibilityFixups = null)
    {
        RootParameter = rootParameter;
        ShaderTypeName = shaderTypeName;
        WireSize = wireSize;
        Storage = storage;
        References = references;
        _scope = scope;
        _compatibilityFixups = compatibilityFixups;
    }

    /// <summary>Gets the logical root parameter name.</summary>
    public string RootParameter { get; }

    /// <summary>Gets the logical shader structure name.</summary>
    public string ShaderTypeName { get; }

    /// <summary>Gets the exact C# wire size before trailing capacity padding.</summary>
    public int WireSize { get; }

    /// <summary>Gets the final native uniform allocation owned by this generation.</summary>
    public IShaderRootStorage Storage { get; }

    /// <summary>Gets immutable, non-owning root reference fixups.</summary>
    public IShaderReferenceMetadata References { get; }

    /// <summary>Writes application arguments once into a new final native allocation.</summary>
    /// <typeparam name="T">The shader argument type.</typeparam>
    /// <param name="value">The input used synchronously and never retained.</param>
    /// <param name="factory">Creates storage with the requested 16-byte-aligned capacity.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="scope">Optional selection preparation and execution admission.</param>
    /// <param name="encode">The stable backend wire encoder, or null for legacy deferred patching.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static ShaderRootArguments Create<T>(in T value, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, IShaderArgumentScope? scope = null, Func<ShaderValue, byte[]>? encode = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : IShaderArguments
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        IShaderRootStorage storage = Allocate(root, name, size, factory);
        try
        {
            var writer = new ShaderTransferWriter<ShaderRootDestination>(new(storage), size, encode, metadataFactory, diagnostic);
            value.Write(ref writer);
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            scope?.Prepare(references);
            return new(root, name, size, storage, references, scope, writer.DeferredFixups);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Writes an unmanaged value once into a new final native allocation.</summary>
    /// <typeparam name="T">The unmanaged wire type.</typeparam>
    /// <param name="value">The value copied synchronously.</param>
    /// <param name="rootParameter">The root parameter name.</param>
    /// <param name="shaderTypeName">The shader type name, or the conventional C# type name.</param>
    /// <param name="factory">Creates storage with the requested 16-byte-aligned capacity.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="scope">Optional selection preparation and execution admission.</param>
    /// <param name="encode">The stable backend wire encoder, or null for legacy deferred patching.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static ShaderRootArguments CreateUnmanaged<T>(in T value, string rootParameter, string? shaderTypeName, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, IShaderArgumentScope? scope = null, Func<ShaderValue, byte[]>? encode = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        IShaderRootStorage storage = Allocate(rootParameter, name, size, factory);
        try
        {
            storage.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)), 0);
            IEnumerable<IShaderReference> empty = Array.Empty<IShaderReference>();
            IShaderReferenceMetadata references = metadataFactory == null ? new ShaderDependencyMetadata(empty) : metadataFactory(empty);
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            diagnostic?.ValidateReferenceCount(0);
            scope?.Prepare(references);
            return new(rootParameter, name, size, storage, references, scope);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Copies an explicit compatibility snapshot once into final native storage.</summary>
    /// <param name="snapshot">The caller-owned snapshot, which is not retained.</param>
    /// <param name="factory">Creates storage with the requested 16-byte-aligned capacity.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="scope">Optional scope overriding the snapshot execution scope.</param>
    /// <param name="encode">The stable backend wire encoder, or null for legacy deferred patching.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static ShaderRootArguments CreateSnapshot(ShaderValueSnapshot snapshot, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, IShaderArgumentScope? scope = null, Func<ShaderValue, byte[]>? encode = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IShaderRootStorage storage = Allocate(snapshot.RootParameter, snapshot.ShaderTypeName, snapshot.Data.Length, factory);
        try
        {
            ShaderDataLayout.ValidateSnapshot(snapshot);
            var writer = new ShaderTransferWriter<ShaderRootDestination>(new(storage), snapshot.Data.Length, encode, metadataFactory, diagnostic);
            writer.WriteRawValue(snapshot.Data.Span);
            foreach (ShaderValue value in snapshot.Values)
            {
                writer.WriteCapturedReference(value);
            }

            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(snapshot.Data.Length, snapshot.GetDiagnosticFields(), snapshot.ShaderTypeName);
            scope ??= snapshot.ExecutionScope;
            scope?.Prepare(references);
            return new(snapshot.RootParameter, snapshot.ShaderTypeName, snapshot.Data.Length, storage, references, scope, writer.DeferredFixups);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Patches reference slots and seals the native allocation on its first successful execution.</summary>
    /// <param name="encode">The backend's stable 16-byte reference encoder.</param>
    public void Seal(Func<ShaderValue, byte[]> encode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(encode);
        if (_sealed)
        {
            return;
        }

        foreach (ShaderValue value in _compatibilityFixups ?? (IEnumerable<ShaderValue>)Array.Empty<ShaderValue>())
        {
            byte[] bytes = encode(value);
            if (bytes.Length != 16)
            {
                throw new ArgumentException("Reference encoding must match the 16-byte ABI.", nameof(encode));
            }

            Storage.Write(bytes, value.Offset);
        }

        Storage.Seal();
        _sealed = true;
    }

    /// <summary>Admits the refreshed complete transitive dependency set for each execution.</summary>
    /// <param name="references">All references reachable by this binding operation.</param>
    public void Admit(IReadOnlyCollection<IShaderReference> references)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scope?.Admit(references);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Storage.Dispose();
    }

    private static IShaderRootStorage Allocate(string root, string name, int size, Func<int, IShaderRootStorage> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        int capacity = Math.Max(16, checked(((size + 15) / 16) * 16));
        IShaderRootStorage storage = factory(capacity) ?? throw new InvalidOperationException("The root storage factory returned null.");
        try
        {
            if (storage.Size != capacity)
            {
                throw new InvalidOperationException("The root allocation must match the requested aligned capacity.");
            }

            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    private static void DisposeFailed(IShaderRootStorage storage)
    {
        try
        {
            storage.Dispose();
        }
        catch
        {
            // Preserve the original selection failure, never cleanup's exception.
        }
    }
}
