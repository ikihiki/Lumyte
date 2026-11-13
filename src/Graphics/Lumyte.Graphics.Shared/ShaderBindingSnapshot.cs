using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures reachable resource identities, with an explicit legacy CPU snapshot adapter.</summary>
public sealed class ShaderBindingSnapshot
{
    private readonly ShaderValueSnapshot? _root;
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> _elements = [];

    private ShaderBindingSnapshot(ShaderValueSnapshot root, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
    {
        _root = root;
        Stack<IShaderReferenceMetadata>? pending = null;
        Collect(new ShaderReferenceMetadata(root.Values).References, table, ref pending, read);
        while (pending != null && pending.TryPop(out IShaderReferenceMetadata? value))
        {
            Collect(value.References, table, ref pending, read);
        }

        Root.ValidateExecutionReferences(_references);
    }

    private ShaderBindingSnapshot(ShaderRootArguments arguments, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read)
    {
        Arguments = arguments;
        Stack<IShaderReferenceMetadata>? pending = null;
        Collect(arguments.References.References, table, ref pending, read);
        while (pending != null && pending.TryPop(out IShaderReferenceMetadata? value))
        {
            Collect(value.References, table, ref pending, read);
        }

        arguments.Admit(_references);
    }

    /// <summary>Gets legacy immutable root values.</summary>
    /// <exception cref="InvalidOperationException">This binding uses native root arguments rather than a legacy snapshot.</exception>
    public ShaderValueSnapshot Root => _root ?? throw new InvalidOperationException("Native root arguments have no numeric CPU snapshot.");

    /// <summary>Gets the command-owned native root generation, or null for a legacy snapshot.</summary>
    public ShaderRootArguments? Arguments { get; }

    /// <summary>Gets the logical root parameter name for either root representation.</summary>
    public string RootParameter => Arguments?.RootParameter ?? Root.RootParameter;

    /// <summary>Gets the reachable resource references.</summary>
    public IReadOnlyCollection<IShaderReference> References => _references;

    /// <summary>Gets the captured shader data elements.</summary>
    public IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), IShaderReferenceMetadata> Elements => _elements;

    /// <summary>Captures a root value and its transitive element dependencies.</summary>
    /// <param name="root">The root input.</param>
    /// <param name="table">The selected table; required when any reachable value contains a resource reference.</param>
    /// <param name="read">The command-local transferred metadata reader.</param>
    /// <returns>The processed result.</returns>
    public static ShaderBindingSnapshot Capture(ShaderValueSnapshot root, IArgumentTable? table, Func<IShaderDataSource, ulong, IShaderReferenceMetadata>? read = null) => new(root, table, read);

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
