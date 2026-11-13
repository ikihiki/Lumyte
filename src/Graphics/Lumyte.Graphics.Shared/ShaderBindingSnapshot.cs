using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures reachable resource identities for command-owned native roots.</summary>
public sealed class ShaderBindingSnapshot
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> _elements = [];

    private ShaderBindingSnapshot(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments;
        Stack<IShaderReferenceMetadata>? pending = null;
        Collect(arguments.References.References, table, ref pending, read);
        while (pending != null && pending.TryPop(out IShaderReferenceMetadata? value))
        {
            Collect(value.References, table, ref pending, read);
        }

        arguments.Admit(_references);
    }

    /// <summary>Gets the command-owned native root generation.</summary>
    public ShaderRootArguments Arguments { get; }

    /// <summary>Gets the logical root parameter name.</summary>
    public string RootParameter => Arguments.RootParameter;

    /// <summary>Gets the reachable resource references.</summary>
    public IReadOnlyCollection<IShaderReference> References => _references;

    /// <summary>Gets the captured shader data elements.</summary>
    public IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> Elements => _elements;

    /// <summary>Captures refreshed transitive dependencies without allocating a numeric root snapshot.</summary>
    /// <param name="arguments">The command-owned native root generation.</param>
    /// <param name="table">The selected argument table.</param>
    /// <param name="read">The command-local transferred metadata reader.</param>
    /// <returns>The processed dependency set.</returns>
    public static ShaderBindingSnapshot Capture(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read = null) => new(arguments, table, read);

    private void Collect(IReadOnlyList<IShaderReference> references, IArgumentTable? table, ref Stack<IShaderReferenceMetadata>? pending, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
    {
        foreach (IShaderReference reference in references)
        {
            if (table is null)
            {
                throw new InvalidOperationException("Select an argument table before drawing or dispatching with resource references.");
            }

            if (!ReferenceEquals(reference.Table, table))
            {
                throw new ArgumentException("Shader resource belongs to a different argument table.", nameof(table));
            }

            if (!_references.Add(reference) || reference.Resource is not IShaderDataSource source)
            {
                continue;
            }

            ulong stride = (ulong)source.Layout.Size;
            ulong start = reference.OffsetInBytes / stride;
            for (ulong i = 0; i < reference.Count; i++)
            {
                (IShaderDataSource, ulong) key = (source, checked(start + i));
                if (_elements.ContainsKey(key))
                {
                    continue;
                }

                IShaderReferenceMetadata snapshot = read == null ? source.ReadReferences(key.Item2) : read(source, key.Item2);
                _elements.Add(key, snapshot);
                (pending ??= new()).Push(snapshot);
            }
        }
    }
}
