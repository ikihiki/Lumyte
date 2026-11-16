namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawingPassPlan2D(DrawingArgumentPlan2D argumentPlan) : IDisposable
{
    private readonly List<IDisposable> _owners = [];
    private readonly List<DrawingPass2D> _passes = [];
    private bool _disposed;

    internal DrawingArgumentPlan2D ArgumentPlan { get; } = argumentPlan;

    internal IReadOnlyList<DrawingPass2D> Passes => _passes;

    public void Dispose()
    {
        _disposed = true;
        while (_owners.Count != 0)
        {
            _owners[^1].Dispose();
            _owners.RemoveAt(_owners.Count - 1);
        }
    }

    internal void Own(IDisposable owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owners.Add(owner);
    }

    internal void Add(DrawingPass2D pass)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _passes.Add(pass);
    }
}
