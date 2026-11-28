namespace Lumyte.Pipelines;

internal sealed class ValueShape : IPipelineBundleVisitor
{
    private readonly bool _actual;
    private readonly object? _publicationProducer;
    private readonly List<SchemaDescriptor?> _schemas = [];
    private int _count;
    private int _depth;

    internal ValueShape(bool actual, object? publicationProducer)
    {
        _actual = actual;
        _publicationProducer = publicationProducer;
    }

    internal bool CpuReadable { get; private set; } = true;

    internal bool IsComposite => _schemas.Count > 1;

    public void Child<T>(T value)
    {
        if (++_count > 64 || ++_depth > 16)
        {
            throw new InvalidOperationException("The explicit bundle shape exceeds its finite traversal limits.");
        }

        try
        {
            if (value is IPipelineBundle bundle)
            {
                _schemas.Add(null);
                bundle.VisitChildren(this);
            }
            else if (value is INonCpuReadablePlacement opaque)
            {
                CpuReadable = false;
                _schemas.Add(opaque.Descriptor);
                if (_actual && opaque.Extent is null)
                {
                    throw new InvalidOperationException("Uninitialized opaque placement in a declared shape.");
                }
            }
            else if (value is IHostPlacement host)
            {
                _schemas.Add(host.Descriptor);
                if (_publicationProducer is not null && !ReferenceEquals(host.Invocation, _publicationProducer))
                {
                    throw new InvalidOperationException("CPU bundle publication requires canonical data copied by this invocation.");
                }

                if (_actual && host.Extent is null)
                {
                    throw new InvalidOperationException("Uninitialized host placement in a declared shape.");
                }
            }
            else
            {
                _schemas.Add(null);
                CpuReadable &= FixedValueMetadata.IsFixed(typeof(T)) || value is ICpuPipelineValue;
            }
        }
        finally
        {
            _depth--;
        }
    }

    internal static ValueShape Capture<T>(T value, bool actual = false, object? publicationProducer = null)
    {
        var shape = new ValueShape(actual, publicationProducer);
        shape.Child(value);
        return shape;
    }

    internal void Validate(ValueShape actual)
    {
        if (_schemas.Count != actual._schemas.Count)
        {
            throw new InvalidOperationException("The immutable bundle shape changed after capture.");
        }

        for (int i = 0; i < _schemas.Count; i++)
        {
            SchemaMetadata.Validate(_schemas[i], actual._schemas[i]);
        }
    }
}
