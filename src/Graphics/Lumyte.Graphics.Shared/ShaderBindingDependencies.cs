using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Collects reachable resource identities for command-owned native roots.</summary>
public sealed class ShaderBindingDependencies
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> _elements = [];

    private ShaderBindingDependencies(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments;
        Stack<IShaderReferenceMetadata>? pending = null;
        VisitReferences(arguments.References.References, table, ref pending, read);
        while (pending != null && pending.TryPop(out IShaderReferenceMetadata? value))
        {
            VisitReferences(value.References, table, ref pending, read);
        }

        arguments.Admit(_references);
    }

    /// <summary>Gets the command-owned native root generation.</summary>
    public ShaderRootArguments Arguments { get; }

    /// <summary>Gets the logical root parameter name.</summary>
    public string RootParameter => Arguments.RootParameter;

    /// <summary>Gets the reachable resource references.</summary>
    public IReadOnlyCollection<IShaderReference> References => _references;

    /// <summary>Gets the reachable shader data element reference metadata.</summary>
    public IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> Elements => _elements;

    /// <summary>Collects refreshed transitive dependencies without allocating a numeric root snapshot.</summary>
    /// <param name="arguments">The command-owned native root generation.</param>
    /// <param name="table">The selected argument table.</param>
    /// <param name="read">The command-local transferred metadata reader.</param>
    /// <returns>The processed dependency set.</returns>
    public static ShaderBindingDependencies Collect(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read = null) => new(arguments, table, read);

    private void VisitReferences(IReadOnlyList<IShaderReference> references, IArgumentTable? table, ref Stack<IShaderReferenceMetadata>? pending, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
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

                IShaderReferenceMetadata metadata = read == null ? source.ReadReferences(key.Item2) : read(source, key.Item2);
                _elements.Add(key, metadata);
                (pending ??= new()).Push(metadata);
            }
        }
    }
}
