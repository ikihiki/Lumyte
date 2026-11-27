using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal sealed class ManagedNativePreparations
{
    private readonly Dictionary<object, (byte[] Bytes, object[] References)> _entries = new(ReferenceEqualityComparer.Instance);

    internal void Prepare<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        object identity = arguments.NativePreparationIdentity ?? throw new InvalidOperationException("Preparation requires a stable non-null identity.");
        var bytes = new List<byte>();
        var references = new List<object>();
        var writer = new ManagedWriter(bytes, references);
        arguments.Write(ref writer);
        if (!_entries.TryAdd(identity, (bytes.ToArray(), references.ToArray())))
        {
            throw new InvalidOperationException("Prepared argument identity reused.");
        }
    }

    internal (byte[] Bytes, object[] References) Consume<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        object identity = arguments.NativePreparationIdentity ?? throw new InvalidOperationException("Record cannot create a native root from raw arguments.");
        return _entries.TryGetValue(identity, out (byte[] Bytes, object[] References) captured) ? captured : throw new InvalidOperationException("Unknown, foreign or expired command-local preparation identity.");
    }
}
