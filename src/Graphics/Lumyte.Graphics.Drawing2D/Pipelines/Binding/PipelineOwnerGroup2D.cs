namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class PipelineOwnerGroup2D : IDisposable
{
    private readonly List<IDisposable> _owners = [];

    public void Dispose()
    {
        var errors = new List<Exception>();
        for (int i = _owners.Count - 1; i >= 0; i--)
        {
            try
            {
                _owners[i].Dispose();
                _owners.RemoveAt(i);
            }
            catch (Exception error)
            {
                errors.Add(error);
                break;
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }
    }

    internal void Add(IDisposable owner) => _owners.Add(owner);
}
