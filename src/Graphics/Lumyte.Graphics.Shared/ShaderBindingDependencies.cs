using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Collects whole-buffer resource dependencies for enabled validation.</summary>
public sealed class ShaderBindingDependencies
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<IShaderDataSource, IShaderReferenceMetadata> _buffers = new(ReferenceEqualityComparer.Instance);

    private ShaderBindingDependencies(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, IShaderReferenceMetadata?>? read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments;
        IShaderReferenceMetadata root = arguments.References ?? throw new NotSupportedException("Root shader dependency metadata is unavailable.");
        Stack<IShaderReferenceMetadata>? pending = null;
        VisitReferences(root.References, table, ref pending, read);
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

    /// <summary>Gets immutable, non-owning metadata for each reachable whole buffer.</summary>
    public IReadOnlyDictionary<IShaderDataSource, IShaderReferenceMetadata> Buffers => _buffers;

    /// <summary>Collects refreshed transitive dependencies without allocating a numeric root snapshot.</summary>
    /// <param name="arguments">The command-owned native root generation.</param>
    /// <param name="table">The selected argument table.</param>
    /// <param name="read">The optional command-local whole-buffer metadata reader.</param>
    /// <returns>The processed dependency set.</returns>
    public static ShaderBindingDependencies Collect(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, IShaderReferenceMetadata?>? read = null) => new(arguments, table, read);

    private void VisitReferences(IReadOnlyList<IShaderReference> references, IArgumentTable? table, ref Stack<IShaderReferenceMetadata>? pending, Func<IShaderDataSource, IShaderReferenceMetadata?>? read)
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

            if (!_references.Add(reference) || reference.Resource is not IShaderDataSource source || _buffers.ContainsKey(source))
            {
                continue;
            }

            IShaderReferenceMetadata? metadata;
            if (read != null)
            {
                metadata = read(source);
            }
            else
            {
                metadata = source.TryGetDependencies(out IShaderReferenceMetadata? dependencies) ? dependencies : null;
            }

            if (metadata == null)
            {
                throw new NotSupportedException("Shader buffer dependency metadata is unavailable.");
            }

            _buffers.Add(source, metadata);
            (pending ??= new()).Push(metadata);
        }
    }
}
