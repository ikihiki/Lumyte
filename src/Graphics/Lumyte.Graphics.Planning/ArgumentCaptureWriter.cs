using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal struct ArgumentCaptureWriter : IShaderValueWriter
{
    private readonly PlanPass _pass;
    private readonly int _size;
    private readonly int _referenceCount;
    private readonly bool _shaderData;
    private int _bytes;
    private int _references;

    internal ArgumentCaptureWriter(PlanPass pass, int size, int referenceCount, bool shaderData = false)
    {
        if (size < 0 || referenceCount < 0 || referenceCount > size / 16)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Static shader capture limits are inconsistent.");
        }

        _pass = pass;
        _size = size;
        _referenceCount = referenceCount;
        _shaderData = shaderData;
        Pieces = [];
    }

    internal List<IArgumentPiece> Pieces { get; }

    /// <inheritdoc/>
    public void WriteValue(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > _size - _bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Capture exceeds its reserved wire size or attempts an empty piece.");
        }

        _bytes += value.Length;
        Pieces.Add(new ValuePiece(value.ToArray()));
    }

    /// <inheritdoc/>
    public void WriteReference<T>(IGpuRef<T>? value)
    {
        if (_size - _bytes < 16 || _references >= _referenceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Capture exceeds its reserved reference or byte count.");
        }

        _bytes += 16;
        _references++;
        if (value is null)
        {
            Pieces.Add(new NullReferencePiece<T>());
        }
        else if (value is IPlanReference symbolic)
        {
            Pieces.Add(_shaderData ? symbolic.CaptureForData(_pass) : symbolic.Capture(_pass));
        }
        else
        {
            throw new ArgumentException("Raw native shader references are forbidden; declare a symbolic binding.");
        }
    }

    internal readonly void Complete<TArguments>()
        where TArguments : struct, IShaderArguments
    {
        if (_bytes != _size || _references != _referenceCount || _size != TArguments.TransferSize || _referenceCount != TArguments.ReferenceCount)
        {
            throw new ArgumentException("Captured arguments do not match their static wire ABI.");
        }
    }
}
