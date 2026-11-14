using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Owns a selected root generation in backend-owned opaque payload storage, retaining only reference metadata.</summary>
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

    /// <summary>Gets the exact logical C# wire byte count, independent of backend storage capacity.</summary>
    public int WireSize { get; }

    /// <summary>Gets the backend-owned opaque payload storage whose logical bytes remain stable for this generation.</summary>
    public IShaderRootStorage Storage { get; }

    /// <summary>Gets immutable, non-owning root resource dependencies.</summary>
    public IShaderReferenceMetadata References { get; }

    /// <summary>Creates an owner for a completed backend-written native generation.</summary>
    /// <param name="rootParameter">The logical root parameter name.</param>
    /// <param name="shaderTypeName">The logical shader type name.</param>
    /// <param name="wireSize">The exact C# wire byte count.</param>
    /// <param name="storage">Completed native storage, transferred only on success.</param>
    /// <param name="references">The immutable backend-owned dependency metadata.</param>
    /// <param name="prepare">Optional reference-only preparation returning detached execution admission.</param>
    /// <returns>The independently owned root generation.</returns>
    /// <remarks>The caller retains storage ownership when validation or preparation throws.</remarks>
    public static ShaderRootArguments Create(string rootParameter, string shaderTypeName, int wireSize, IShaderRootStorage storage, IShaderReferenceMetadata references, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderTypeName);
        ArgumentOutOfRangeException.ThrowIfNegative(wireSize);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(references);
        if (storage.Size < 0 || storage.Size < wireSize)
        {
            throw new InvalidOperationException("The root storage capacity must be nonnegative and cover the logical wire bytes.");
        }

        Action<IReadOnlyCollection<IShaderReference>>? admit = prepare?.Invoke(references);
        return new(rootParameter, shaderTypeName, wireSize, storage, references, admit);
    }

    /// <summary>Allocates validated final native storage for a backend writer.</summary>
    /// <param name="root">The logical root parameter name.</param>
    /// <param name="name">The logical shader type name.</param>
    /// <param name="size">The exact C# wire byte count.</param>
    /// <param name="factory">Creates backend-owned storage for the exact logical wire byte count.</param>
    /// <returns>Caller-owned storage; a backend must dispose it if writing fails.</returns>
    public static IShaderRootStorage Allocate(string root, string name, int size, Func<int, IShaderRootStorage> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        IShaderRootStorage storage = factory(size) ?? throw new InvalidOperationException("The root storage factory returned null.");
        try
        {
            if (storage.Size < 0 || storage.Size < size)
            {
                throw new InvalidOperationException("The root storage capacity must be nonnegative and cover the logical wire bytes.");
            }

            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Commits and freezes the already encoded backend payload on its first successful execution.</summary>
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
