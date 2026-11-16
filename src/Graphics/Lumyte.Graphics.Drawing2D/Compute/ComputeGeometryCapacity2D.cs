namespace Lumyte.Graphics.Drawing2D;

/// <summary>
/// Conservative element counts and owned bytes. WorkingBytes includes raw managed inputs,
/// upload and GPU inputs, generated edges/triangles, references, backdrop, scan and overhead.
/// Does not include output color, descriptor tables, paint/clip, or other executor allocations:
/// caller must add those before Validate. Packed shader ABI must not exceed the charged strides
/// (96 input, 32 edge, 48 triangle, 16 reference/draw-reference, 8 backdrop/scan bytes).
/// </summary>
internal readonly record struct ComputeGeometryCapacity2D(
    long Edges,
    long Triangles,
    long EdgeReferences,
    long DrawReferences,
    long BackdropElements,
    long ScanElements,
    long GeometryVectors,
    long WorkingBytes)
{
    internal ComputeGeometryCapacity2D Combine(ComputeGeometryCapacity2D other) => new(
        checked(Edges + other.Edges),
        checked(Triangles + other.Triangles),
        checked(EdgeReferences + other.EdgeReferences),
        checked(DrawReferences + other.DrawReferences),
        checked(BackdropElements + other.BackdropElements),
        checked(ScanElements + other.ScanElements),
        checked(GeometryVectors + other.GeometryVectors),
        checked(WorkingBytes + other.WorkingBytes));

    internal void Validate(long maxWorkingBytes, long paintClipVectors = 0, long otherWorkingBytes = 0)
    {
        if (maxWorkingBytes <= 0 || paintClipVectors < 0 || otherWorkingBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWorkingBytes));
        }

        try
        {
            if (checked(EdgeReferences + DrawReferences) > 4194304 ||
                checked(GeometryVectors + paintClipVectors) > 262144 ||
                checked(WorkingBytes + otherWorkingBytes) > maxWorkingBytes)
            {
                throw new NotSupportedException("Compute geometry exceeds the cumulative preparation budget.");
            }
        }
        catch (OverflowException exception)
        {
            throw new NotSupportedException("Compute geometry cumulative budget overflow.", exception);
        }
    }
}
