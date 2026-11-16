using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal ref struct ValidationWriter<TWriter> : IShaderValueWriter
    where TWriter : IShaderValueWriter, allows ref struct
{
    internal readonly HashSet<IShaderReference> References;
    internal TWriter Inner;
    private readonly ValidationWriteContext _context;
    private readonly int _size;
    private readonly int _expectedReferences;

    private int _position;
    private int _references;

    internal ValidationWriter(TWriter inner, ValidationWriteContext context, int size, int expectedReferences)
    {
        Inner = inner;
        _context = context;
        _size = size;
        _expectedReferences = expectedReferences;
        References = new(ReferenceEqualityComparer.Instance);
        _position = 0;
        _references = 0;
    }

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        Check(value.Length);
        Inner.WriteValue(value);
        _position += value.Length;
    }

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        Check(16);
        if (_references >= _expectedReferences)
        {
            throw new InvalidOperationException("Shader writer exceeds its declared reference count.");
        }

        if (value is not IValidationReference reference || !ReferenceEquals(reference.Device, _context.Device))
        {
            throw new ArgumentException("Validation: null, foreign, or unvalidated shader reference.", nameof(value));
        }

        reference.ValidateRegistration();
        _context.Diagnostic?.ValidateReference(typeof(T), reference, _position);
        reference.WriteNative(ref Inner);
        References.Add(reference);
        _context.References.Add(reference);
        _position += 16;
        ++_references;
    }

    internal readonly void Complete()
    {
        if (_position != _size || _references != _expectedReferences)
        {
            throw new InvalidOperationException("Shader writer did not emit its declared byte size and reference count.");
        }

        _context.Diagnostic?.ValidateReferenceCount(_references);
    }

    private readonly void Check(int length)
    {
        if (length < 0 || length > _size - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Shader writer exceeds its declared transfer size.");
        }
    }
}
