using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures reachable CPU shader values and resource registrations.</summary>
public sealed class ShaderBindingSnapshot
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> _elements = [];

    private ShaderBindingSnapshot(ShaderValueSnapshot root, IArgumentTable? table, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read)
    {
        Root = root;
        Stack<ShaderValueSnapshot>? pending = null;
        Collect(root, table, ref pending, read);
        while (pending != null && pending.TryPop(out ShaderValueSnapshot? value))
        {
            Collect(value, table, ref pending, read);
        }
    }

    /// <summary>Gets the immutable root values.</summary>
    public ShaderValueSnapshot Root { get; }

    /// <summary>Gets the reachable resource references.</summary>
    public IReadOnlyCollection<IShaderReference> References => _references;

    /// <summary>Gets the captured shader data elements.</summary>
    public IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> Elements => _elements;

    /// <summary>Captures a root value and its transitive element dependencies.</summary>
    /// <param name="root">The root input.</param>
    /// <param name="table">The selected table; required when any reachable value contains a resource reference.</param>
    /// <param name="read">The command-local transferred metadata reader.</param>
    /// <returns>The processed result.</returns>
    public static ShaderBindingSnapshot Capture(ShaderValueSnapshot root, IArgumentTable? table, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read = null) => new(root, table, read);

    private void Collect(ShaderValueSnapshot value, IArgumentTable? table, ref Stack<ShaderValueSnapshot>? pending, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read)
    {
        foreach (ShaderValue member in value.Values)
        {
            if (!member.IsReference)
            {
                continue;
            }

            if (member.Reference is not IShaderReference reference)
            {
                throw new ArgumentException("A shader reference must originate from an argument table.");
            }

            if (table is null)
            {
                throw new InvalidOperationException("Select an argument table before drawing or dispatching with resource references.");
            }

            if (!ReferenceEquals(reference.Table, table))
            {
                throw new ArgumentException($"Shader resource '{member.Path}' belongs to a different argument table.", nameof(table));
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

                ShaderValueSnapshot snapshot = read == null ? source.Read(key.Item2) : read(source, key.Item2);
                _elements.Add(key, snapshot);
                (pending ??= new()).Push(snapshot);
            }
        }
    }
}
