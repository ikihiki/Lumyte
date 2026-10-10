using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures reachable CPU shader values and resource registrations.</summary>
public sealed class ShaderBindingSnapshot
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> _elements = [];

    private ShaderBindingSnapshot(ShaderValueSnapshot root, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read)
    {
        Root = root;
        Stack<ShaderValueSnapshot>? pending = null;
        Collect(root, ref pending, read);
        while (pending != null && pending.TryPop(out ShaderValueSnapshot? value))
        {
            Collect(value, ref pending, read);
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
    /// <returns>The processed result.</returns>
    /// <param name="read">The command-local transferred metadata reader.</param>
    public static ShaderBindingSnapshot Capture(ShaderValueSnapshot root, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read = null) => new(root, read);

    private void Collect(ShaderValueSnapshot value, ref Stack<ShaderValueSnapshot>? pending, Func<IShaderDataSource, ulong, ShaderValueSnapshot>? read)
    {
        foreach (ShaderValue member in value.Values)
        {
            if (!member.IsReference)
            {
                continue;
            }

            var reference = (IShaderReference)member.Reference!;
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
