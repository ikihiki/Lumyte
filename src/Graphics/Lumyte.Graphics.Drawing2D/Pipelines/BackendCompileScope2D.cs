using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class BackendCompileScope2D(IGraphicDevice device, IDisposable? externalOwner, IReadOnlySet<PipelineDeclaration2D> declarations) : IDisposable
{
    private readonly Dictionary<object, object> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _owned = [];
    private readonly HashSet<PipelineDeclaration2D> _creating = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDisposable> _ownedIdentities = new(ReferenceEqualityComparer.Instance);

    private bool _committed;

    public void Dispose()
    {
        while (_owned.Count != 0)
        {
            int last = _owned.Count - 1;
            _owned[last].Dispose();
            _owned.RemoveAt(last);
        }

        if (!_committed && externalOwner is PipelineElement2D externalElement)
        {
            externalElement.ReleaseReservation(this);
        }
    }

    internal void Commit() => _committed = true;

    internal object Resolve(PipelineDeclaration2D declaration)
    {
        if (!declarations.Contains(declaration))
        {
            throw new ArgumentException("Child resolution requires an explicitly declared passive child topology.");
        }

        if (_instances.TryGetValue(declaration, out object? existing))
        {
            return existing;
        }

        if (!_creating.Add(declaration))
        {
            throw new ArgumentException("Recursive pipeline declaration construction is not supported.");
        }

        try
        {
            var context = new PipelineBuildContext2D(device, this);
            object result;
            try
            {
                result = declaration.CreateInstance(context);
            }
            finally
            {
                context.Close();
            }

            if (declaration.OwnsProcessor && result is PipelineElement2D element)
            {
                element.Adopt(declaration, this);
            }

            if (declaration.OwnsProcessor && result is IDisposable owner && !ReferenceEquals(owner, externalOwner) && _ownedIdentities.Add(owner))
            {
                _owned.Add(owner);
            }

            declaration.ValidateInstance(result);
            _instances.Add(declaration, result);
            return result;
        }
        finally
        {
            _creating.Remove(declaration);
        }
    }
}
