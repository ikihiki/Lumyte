namespace Lumyte.Graphics.Drawing2D;

internal static class DrawingSceneCapture2D
{
    internal static DrawList2D Capture(IDrawingSceneSource2D source, CancellationToken cancellationToken, long maxWorkingBytes = 256L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(source);
        long captureBytes = ValidateCapture(source, maxWorkingBytes, cancellationToken);

        var commands = new List<DrawingCommand2D>();
        foreach (DrawingSceneEntry2D entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawList2D transformed = Renderer2D.TransformPages(entry.Content.RetainedPages.Pages.ToArray(), entry.Transform, entry.Clips);
            commands.AddRange(transformed.Commands);
        }

        _ = source.Revision;
        return new(commands) { CapturedWorkingBytes = captureBytes };
    }

    internal static long ValidateCapture(IDrawingSceneSource2D source, long maximum, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        long bytes = 0;
        Charge(4096);
        foreach (DrawingSceneEntry2D entry in source.Entries)
        {
            token.ThrowIfCancellationRequested();

            // Includes empty placements, scene-key entries and page-list growth.
            Charge(1024 + (entry.Content.Count * 1024L) + (entry.Clips.Count * 128L));
            var clips = new HashSet<ClipStack2D>(ReferenceEqualityComparer.Instance);
            foreach (DrawList2D page in entry.Content.RetainedPages.Pages)
            {
                Charge(512);
                foreach (DrawingCommand2D command in page.Commands)
                {
                    if (!clips.Contains(command.State.Clip))
                    {
                        // TransformPages creates one combined list and snapshot per
                        // distinct local stack, replicating all parent clip elements.
                        Charge(512 + ((entry.Clips.Count + (long)command.State.Clip.Elements.Count) * 256));
                        clips.Add(command.State.Clip);
                    }
                }
            }
        }

        return bytes;

        void Charge(long value)
        {
            bytes = checked(bytes + value);
            if (bytes > maximum)
            {
                throw new NotSupportedException("Scene capture exceeds MaxWorkingBytes before materialization.");
            }
        }
    }
}
