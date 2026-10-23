using System.Threading.Channels;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestSource : IResourceSource, IResourceChangeSource
{
    private readonly object _gate = new();
    private readonly Channel<ResourceSourceChange> _changes = Channel.CreateBounded<ResourceSourceChange>(16);
    private float _sample = 0.25f;
    private int _revision = 1;

    public IResourceChangeSource Changes => this;

    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<ResourceSourceSnapshot>(new TestSnapshot(id, _revision.ToString(System.Globalization.CultureInfo.InvariantCulture), id.Value == "invalid" ? float.NaN : _sample));
        }
    }

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<string?>(_revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default) => _changes.Reader.ReadAllAsync(cancellationToken);

    internal void Set(float sample)
    {
        lock (_gate)
        {
            _sample = sample;
            _revision++;
            if (!_changes.Writer.TryWrite(new(new ResourceId("tone"), null)))
            {
                throw new InvalidOperationException("Sample notification queue overflowed.");
            }
        }
    }
}
