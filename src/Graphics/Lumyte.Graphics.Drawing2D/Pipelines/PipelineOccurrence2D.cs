namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>A frozen structural placement, never a configuration-reference identity.</summary>
internal sealed record PipelineOccurrence2D(PipelineDeclaration2D Declaration, string Path, PipelineOccurrence2D[] Children)
{
    internal IEnumerable<string> EnumeratePaths()
    {
        yield return Path;
        foreach (PipelineOccurrence2D child in Children)
        {
            foreach (string path in child.EnumeratePaths())
            {
                yield return path;
            }
        }
    }
}
