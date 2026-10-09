using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Tracks explicit shader data copies within one command recording.</summary>
public sealed class ShaderDataTransferState
{
    private readonly HashSet<(IShaderDataSource Source, ulong Element)> _observed = [];
    private readonly Dictionary<(IShaderDataSource Source, ulong Element), ShaderValueSnapshot> _pending = [];

    /// <summary>Rejects cross-command uploads whose new metadata was unavailable during a later draw recording.</summary>
    /// <param name="commands">The transfer states in submission order.</param>
    public static void ValidateSubmission(IEnumerable<ShaderDataTransferState> commands)
    {
        var written = new HashSet<(IShaderDataSource Source, ulong Element)>();
        foreach (ShaderDataTransferState command in commands)
        {
            if (command._observed.Overlaps(written))
            {
                throw new InvalidOperationException("Record dependent draws after the upload is submitted, or in the upload command itself.");
            }

            written.UnionWith(command._pending.Keys);
        }
    }

    /// <summary>Validates and records a staging copy and its CPU dependency metadata.</summary>
    /// <param name="source">The upload staging source.</param>
    /// <param name="sourceOffset">The source element offset.</param>
    /// <param name="destination">The GPU destination.</param>
    /// <param name="destinationOffset">The destination element offset.</param>
    /// <param name="count">The element count.</param>
    /// <param name="copy">The explicit native copy recorder.</param>
    /// <param name="track">The command lifetime validator registration.</param>
    public void Record(IShaderDataSource source, ulong sourceOffset, IShaderDataSource destination, ulong destinationOffset, ulong count, Action copy, Action<Action> track)
    {
        source.ValidateAlive();
        destination.ValidateAlive();
        if (source.Memory != MemoryPreference.Upload || destination.Memory != MemoryPreference.Automatic || !source.Layout.Matches(destination.Layout))
        {
            throw new ArgumentException("Copy requires upload staging and a compatible GPU shader data layout.");
        }

        // Reading the native handle also rejects mapped or pending mappings.
        _ = source.ShaderHandle;
        _ = destination.ShaderHandle;
        var values = new ShaderValueSnapshot[checked((int)count)];
        for (ulong i = 0; i < count; i++)
        {
            values[checked((int)i)] = source.Read(checked(sourceOffset + i));
            foreach (ShaderValue member in values[checked((int)i)].Values)
            {
                (member.Reference as IShaderReference)?.Validate();
            }
        }

        copy();
        track(() =>
        {
            source.ValidateAlive();
            destination.ValidateAlive();
            for (ulong i = 0; i < count; i++)
            {
                if (!ReferenceEquals(source.Read(checked(sourceOffset + i)), values[checked((int)i)]))
                {
                    throw new InvalidOperationException("Copied staging elements were rewritten after copy recording.");
                }
            }

            foreach (ShaderValueSnapshot value in values)
            {
                foreach (ShaderValue member in value.Values)
                {
                    (member.Reference as IShaderReference)?.Validate();
                }
            }
        });
        for (ulong i = 0; i < count; i++)
        {
            _pending[(destination, checked(destinationOffset + i))] = values[checked((int)i)];
        }
    }

    /// <summary>Reads the metadata visible at this point in command order.</summary>
    /// <param name="source">The GPU source.</param>
    /// <param name="index">The element index.</param>
    /// <param name="track">The command lifetime validator registration.</param>
    /// <returns>The transferred element metadata.</returns>
    public ShaderValueSnapshot Read(IShaderDataSource source, ulong index, Action<Action> track)
    {
        source.ValidateAlive();
        if (_pending.TryGetValue((source, index), out ShaderValueSnapshot? value))
        {
            return value;
        }

        value = source.Read(index);
        _observed.Add((source, index));
        track(() =>
        {
            if (!ReferenceEquals(source.Read(index), value))
            {
                throw new InvalidOperationException("Shader data was transferred again after this command was recorded.");
            }
        });
        return value;
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
