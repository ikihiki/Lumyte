namespace Lumyte.Graphics.Drawing2D;

internal static class DrawingSceneCapture2D
{
    internal static DrawList2D Capture(IDrawingSceneSource2D source, CancellationToken cancellationToken, long maxWorkingBytes = 256L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(source);
        long count = 0;
        foreach (DrawingSceneEntry2D entry in source.Entries)
        {
            count = checked(count + entry.Content.Count);
            if (checked(4096L + (count * 1024L)) > maxWorkingBytes)
            {
                throw new NotSupportedException("Scene capture exceeds the preallocation working budget.");
            }
        }

        var commands = new List<DrawingCommand2D>();
        foreach (DrawingSceneEntry2D entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawList2D transformed = Renderer2D.TransformPages(entry.Content.RetainedPages.Pages.ToArray(), entry.Transform, entry.Clips);
            commands.AddRange(transformed.Commands);
        }

        _ = source.Revision;
        return new(commands);
    }
}
