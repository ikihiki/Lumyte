using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Tracks explicit shader data copies within one command recording.</summary>
public sealed class ShaderDataTransferState
{
    private readonly Dictionary<(IShaderDataSource Source, ulong Element), ShaderValueSnapshot> _pending = [];

    private List<(IShaderDataSource Source, ulong Index, ShaderValueSnapshot Value)>? _validation;

    /// <summary>Enables copy metadata revision checks for this command.</summary>
    public void EnableValidation() => _validation ??= [];

    /// <summary>Rejects changed source metadata before native submission.</summary>
    public void ValidateSubmit()
    {
        if (_validation == null)
        {
            return;
        }

        foreach ((IShaderDataSource source, ulong index, ShaderValueSnapshot value) in _validation)
        {
            if (!ReferenceEquals(source.Read(index), value))
            {
                throw new InvalidOperationException("Validation: copy source metadata changed after recording.");
            }
        }
    }

    /// <summary>Records a staging copy and its CPU dependency metadata.</summary>
    /// <param name="source">The upload staging source.</param>
    /// <param name="sourceOffset">The source element offset.</param>
    /// <param name="destination">The GPU destination.</param>
    /// <param name="destinationOffset">The destination element offset.</param>
    /// <param name="count">The element count.</param>
    /// <param name="copy">The explicit native copy recorder.</param>
    public void Record(IShaderDataSource source, ulong sourceOffset, IShaderDataSource destination, ulong destinationOffset, ulong count, Action copy)
    {
        var values = new ShaderValueSnapshot[checked((int)count)];
        for (ulong i = 0; i < count; i++)
        {
            values[checked((int)i)] = source.Read(checked(sourceOffset + i));
            _validation?.Add((source, checked(sourceOffset + i), values[checked((int)i)]));
        }

        copy();
        for (ulong i = 0; i < count; i++)
        {
            _pending[(destination, checked(destinationOffset + i))] = values[checked((int)i)];
        }
    }

    /// <summary>Reads the metadata visible at this point in command order.</summary>
    /// <param name="source">The GPU source.</param>
    /// <param name="index">The element index.</param>
    /// <returns>The transferred element metadata.</returns>
    public ShaderValueSnapshot Read(IShaderDataSource source, ulong index)
    {
        if (_pending.TryGetValue((source, index), out ShaderValueSnapshot? value))
        {
            return value;
        }

        return source.Read(index);
    }

    /// <summary>Publishes dependency metadata after native queue submission succeeds.</summary>
    public void Publish()
    {
        foreach (((IShaderDataSource source, ulong index), ShaderValueSnapshot value) in _pending)
        {
            source.SetTransferredValue(index, value);
        }
    }
}
