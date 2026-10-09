using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics;

internal sealed class ShaderBindingSnapshot
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

    internal ShaderValueSnapshot Root { get; }

    internal IReadOnlyCollection<IShaderReference> References => _references;

    internal IReadOnlyDictionary<(IShaderDataSource Buffer, ulong Element), ShaderValueSnapshot> Elements => _elements;

    internal static ShaderBindingSnapshot Capture(object table, ShaderValueSnapshot root) => new(table, root);

    internal void ValidateLayouts(ShaderTargetData target)
    {
        foreach (IShaderDataSource source in _elements.Keys.Select(k => k.Buffer).Distinct())
        {
            if (!source.Layout.Matches(ShaderDataLayout.Data(target, source.Layout.Name)))
            {
                throw new ArgumentException("Shader data schema differs from the consuming program.");
            }
        }
    }

    internal void Validate()
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
