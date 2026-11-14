// Test-only producer seam; production accepts already-written storage.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Tests;

internal static class TestShaderTransfer
{
    /// <summary>Writes application arguments once into a new final native allocation.</summary>
    /// <typeparam name="T">The shader argument type.</typeparam>
    /// <param name="value">The input used synchronously and never retained.</param>
    /// <param name="factory">Creates backend-owned storage for the exact logical wire byte count.</param>
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
        IShaderRootStorage storage = ShaderRootArguments.Allocate(root, name, size, factory);
        try
        {
            var writer = new TestShaderValueWriter<TestShaderRootDestination>(new(storage), size, encode, metadataFactory, diagnostic);
            value.Write(ref writer);
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            return ShaderRootArguments.Create(root, name, size, storage, references, prepare);
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
    /// <param name="factory">Creates backend-owned storage for the exact logical wire byte count.</param>
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
        IShaderRootStorage storage = ShaderRootArguments.Allocate(rootParameter, name, size, factory);
        try
        {
            var writer = new TestShaderValueWriter<TestShaderRootDestination>(new(storage), size, encode ?? RejectReference, metadataFactory, diagnostic);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            diagnostic?.ValidateReferenceCount(0);
            return ShaderRootArguments.Create(rootParameter, name, size, storage, references, prepare);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    private static byte[] RejectReference(ShaderValue value) => throw new InvalidOperationException("Unmanaged values cannot emit references.");

    private static void DisposeFailed(IShaderRootStorage storage)
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
