// Test-only host sink and owner; not a production common-storage contract.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Tests;

internal static class TestShaderTransfer
{
    /// <summary>Writes application arguments once into a new fake-backend host sink.</summary>
    /// <typeparam name="T">The shader argument type.</typeparam>
    /// <param name="value">The input used synchronously and never retained.</param>
    /// <param name="factory">Creates backend-owned storage for the exact logical wire byte count.</param>
    /// <param name="encode">The stable synchronous backend reference encoder.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="prepare">Optional reference-only selection preparation returning detached execution admission.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static TestRootOwner Create<T>(in T value, Func<int, ITestShaderRootStorage> factory, Func<ShaderValue, byte[]> encode, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
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
        ITestShaderRootStorage storage = Allocate(size, factory);
        try
        {
            var writer = new TestShaderValueWriter<TestShaderRootDestination>(new(storage), size, encode, metadataFactory, diagnostic);
            value.Write(ref writer);
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            return new(storage, ShaderRootArguments.Create(root, name, size, references, prepare));
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    /// <summary>Writes an unmanaged value once into a new fake-backend host sink.</summary>
    /// <typeparam name="T">The unmanaged wire type.</typeparam>
    /// <param name="value">The value copied synchronously.</param>
    /// <param name="rootParameter">The root parameter name.</param>
    /// <param name="shaderTypeName">The shader type name, or the conventional C# type name.</param>
    /// <param name="factory">Creates backend-owned storage for the exact logical wire byte count.</param>
    /// <param name="diagnostic">Optional explicitly enabled shader layout diagnostics.</param>
    /// <param name="prepare">Optional reference-only selection preparation returning detached execution admission.</param>
    /// <param name="encode">The stable synchronous backend reference encoder.</param>
    /// <param name="metadataFactory">The immutable backend dependency owner factory.</param>
    /// <returns>The independently owned root generation.</returns>
    public static TestRootOwner CreateUnmanaged<T>(in T value, string rootParameter, string? shaderTypeName, Func<int, ITestShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null, Func<ShaderValue, byte[]>? encode = null, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        ITestShaderRootStorage storage = Allocate(size, factory);
        try
        {
            var writer = new TestShaderValueWriter<TestShaderRootDestination>(new(storage), size, encode ?? RejectReference, metadataFactory, diagnostic);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            diagnostic?.ValidateReferenceCount(0);
            return new(storage, ShaderRootArguments.Create(rootParameter, name, size, references, prepare));
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    private static ITestShaderRootStorage Allocate(int size, Func<int, ITestShaderRootStorage> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ITestShaderRootStorage storage = factory(size) ?? throw new InvalidOperationException("Test sink factory returned null.");
        if (storage.Size < size)
        {
            DisposeFailed(storage);
            throw new InvalidOperationException("Test sink is too small.");
        }

        return storage;
    }

    private static byte[] RejectReference(ShaderValue value) => throw new InvalidOperationException("Unmanaged values cannot emit references.");

    private static void DisposeFailed(ITestShaderRootStorage storage)
    {
        try
        {
            storage.Dispose();
        }
        catch
        {
            // Preserve the original producer failure.
        }
    }
}
