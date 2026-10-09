using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures reachable CPU shader values and resource registrations.</summary>
public sealed class ShaderBindingSnapshot
{
    private readonly HashSet<IShaderReference> _references = [];
    private readonly Dictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> _elements = [];

    private ShaderBindingSnapshot(object table, ShaderValueSnapshot root)
    {
        Root = root;
        Stack<ShaderValueSnapshot>? pending = null;
        Collect(table, root, ref pending);
        while (pending != null && pending.TryPop(out ShaderValueSnapshot? value))
        {
            Collect(table, value, ref pending);
        }
    }

    /// <summary>Gets the immutable root values.</summary>
    public ShaderValueSnapshot Root { get; }

    /// <summary>Gets the reachable resource references.</summary>
    public IReadOnlyCollection<IShaderReference> References => _references;

    /// <summary>Gets the captured shader data elements.</summary>
    public IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> Elements => _elements;

    /// <summary>Captures a root value and its transitive element dependencies.</summary>
    /// <param name="table">The table input.</param>
    /// <param name="root">The root input.</param>
    /// <returns>The processed result.</returns>
    public static ShaderBindingSnapshot Capture(object table, ShaderValueSnapshot root) => new(table, root);

    /// <summary>Validates captured element schemas against the consuming shader.</summary>
    /// <param name="target">The target input.</param>
    public void ValidateLayouts(ShaderTargetData target)
    {
        foreach (IShaderDataSource source in _elements.Keys.Select(k => k.Buffer).Distinct())
        {
            if (!source.Layout.Matches(ShaderDataLayout.Data(target, source.Layout.Name)))
            {
                throw new ArgumentException("Shader data schema differs from the consuming program.");
            }
        }
    }

    /// <summary>Validates the captured registration and resource lifetimes.</summary>
    public void Validate()
    {
        foreach (IShaderReference reference in _references)
        {
            reference.Validate();
            if (reference.Resource is IShaderRawBuffer raw)
            {
                _ = raw.ShaderHandle;
            }
        }

        foreach ((IShaderDataSource buffer, _) in _elements.Keys)
        {
            buffer.ValidateAlive();
        }
    }

    private void Collect(object table, ShaderValueSnapshot value, ref Stack<ShaderValueSnapshot>? pending)
    {
        foreach (ShaderValue member in value.Values)
        {
            if (!member.IsReference)
            {
                continue;
            }

            if (member.Reference is not IShaderReference reference || !ReferenceEquals(reference.Table, table))
            {
                throw new ArgumentException("Every reachable reference must belong to the selected argument table.");
            }

            reference.Validate();
            if (!_references.Add(reference) || reference.Resource is not IShaderDataSource source)
            {
                continue;
            }

            ulong stride = (ulong)source.Layout.Size;
            if (reference.OffsetInBytes % stride != 0 || reference.SizeInBytes != checked(reference.Count * stride))
            {
                throw new ArgumentException("Shader data reference is not element-aligned.");
            }

            ulong start = reference.OffsetInBytes / stride;
            for (ulong i = 0; i < reference.Count; i++)
            {
                (IShaderDataSource, ulong) key = (source, checked(start + i));
                if (_elements.ContainsKey(key))
                {
                    continue;
                }

                ShaderValueSnapshot snapshot = source.Read(key.Item2);
                _elements.Add(key, snapshot);
                (pending ??= new()).Push(snapshot);
            }
        }
    }
}
