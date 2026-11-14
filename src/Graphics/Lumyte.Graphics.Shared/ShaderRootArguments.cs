using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Owns a selected root generation in final native storage, retaining only reference metadata.</summary>
public sealed class ShaderRootArguments : IDisposable
{
    private readonly Action<IReadOnlyCollection<IShaderReference>>? _admit;
    private bool _sealed;
    private bool _disposed;

    private ShaderRootArguments(string rootParameter, string shaderTypeName, int wireSize, IShaderRootStorage storage, IShaderReferenceMetadata references, Action<IReadOnlyCollection<IShaderReference>>? admit)
    {
        RootParameter = rootParameter;
        ShaderTypeName = shaderTypeName;
        WireSize = wireSize;
        Storage = storage;
        References = references;
        _admit = admit;
    }

    /// <summary>Gets the logical root parameter name.</summary>
    public string RootParameter { get; }

    /// <summary>Gets the logical shader structure name.</summary>
    public string ShaderTypeName { get; }

    /// <summary>Gets the exact C# wire size before trailing capacity padding.</summary>
    public int WireSize { get; }

    /// <summary>Gets the final native uniform allocation owned by this generation.</summary>
    public IShaderRootStorage Storage { get; }

    /// <summary>Gets immutable, non-owning root resource dependencies.</summary>
    public IShaderReferenceMetadata References { get; }

    /// <summary>Writes application arguments once into a new final native allocation.</summary>
    /// <typeparam name="T">The shader argument type.</typeparam>
    /// <param name="value">The input used synchronously and never retained.</param>
    /// <param name="factory">Creates storage with the requested 16-byte-aligned capacity.</param>
    /// <param name="encode">The stable synchronous backend reference encoder.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="prepare">Optional reference-only selection preparation returning detached execution admission.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static ShaderRootArguments Create<T>(in T value, Func<int, IShaderRootStorage> factory, Func<ShaderValue, byte[]> encode, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : IShaderArguments
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        ArgumentNullException.ThrowIfNull(encode);
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
            Action<IReadOnlyCollection<IShaderReference>>? admit = prepare?.Invoke(references);
            return new(root, name, size, storage, references, admit);
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
    /// <param name="prepare">Optional reference-only selection preparation returning detached execution admission.</param>
    /// <param name="encode">The stable synchronous backend reference encoder.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static ShaderRootArguments CreateUnmanaged<T>(in T value, string rootParameter, string? shaderTypeName, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null, Func<ShaderValue, byte[]>? encode = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        IShaderRootStorage storage = Allocate(rootParameter, name, size, factory);
        try
        {
            var writer = new ShaderTransferWriter<ShaderRootDestination>(new(storage), size, encode ?? RejectReference, metadataFactory, diagnostic);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            diagnostic?.ValidateReferenceCount(0);
            Action<IReadOnlyCollection<IShaderReference>>? admit = prepare?.Invoke(references);
            return new(rootParameter, name, size, storage, references, admit);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Seals the already encoded native allocation on its first successful execution.</summary>
    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
        {
            return;
        }

        Storage.Seal();
        _sealed = true;
    }

    /// <summary>Admits the refreshed complete transitive dependency set for each execution.</summary>
    /// <param name="references">All references reachable by this binding operation.</param>
    public void Admit(IReadOnlyCollection<IShaderReference> references)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _admit?.Invoke(references);
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

    private static byte[] RejectReference(ShaderValue value)
        => throw new InvalidOperationException("A raw unmanaged value cannot emit resource references.");

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
