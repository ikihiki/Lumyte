namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>The fixed correlated sixteen-sample grid.</summary>
public sealed record SamplePolicy2D(int Count, int GridVersion)
{
    internal void Validate()
    {
        if (Count != 16 || GridVersion != 1)
        {
            throw new NotSupportedException("Only Count=16/GridVersion=1 is canonical.");
        }
    }
}
