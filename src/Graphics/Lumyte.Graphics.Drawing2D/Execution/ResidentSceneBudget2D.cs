namespace Lumyte.Graphics.Drawing2D;

internal static class ResidentSceneBudget2D
{
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
            foreach (DrawList2D page in entry.Content.RetainedPages.Pages)
            {
                Charge(512);
                foreach (DrawingCommand2D command in page.Commands)
                {
                    // TransformPages creates one combined list and snapshot per
                    // distinct local stack, replicating all parent clip elements.
                    Charge(512 + ((entry.Clips.Count + (long)command.State.Clip.Elements.Count) * 256));
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
