namespace Lumyte.Resources.Sample;

internal sealed class SampleCounters
{
    private int _documentBuilds;
    private int _projectionBuilds;

    internal int DocumentBuilds => Volatile.Read(ref _documentBuilds);

    internal int ProjectionBuilds => Volatile.Read(ref _projectionBuilds);

    internal void RecordDocumentBuild() => Interlocked.Increment(ref _documentBuilds);

    internal void RecordProjectionBuild() => Interlocked.Increment(ref _projectionBuilds);
}
