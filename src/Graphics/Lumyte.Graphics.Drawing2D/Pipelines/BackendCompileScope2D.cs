using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class BackendCompileScope2D(IGraphicDevice device, IDisposable? externalOwner) : IDisposable
{
    private readonly Dictionary<PipelineOccurrence2D, object> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _owned = [];
    private readonly HashSet<PipelineOccurrence2D> _creating = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDisposable> _ownedIdentities = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<(Type Service, string Kind, object Options), (Type Factory, int Version, PipelineSharedService2D Value)> _services = [];
    private readonly HashSet<(Type Service, string Kind, object Options)> _creatingServices = [];
    private bool _committed;

    public void Dispose()
    {
        while (_owned.Count != 0)
        {
            int last = _owned.Count - 1;
            _owned[last].Dispose();
            _owned.RemoveAt(last);
        }

        if (!_committed && externalOwner is PipelineSharedService2D externalService)
        {
            externalService.ReleaseReservation(this);
        }

        if (!_committed && externalOwner is PipelineElement2D externalElement)
        {
            externalElement.ReleaseReservation(this);
        }
    }

    internal TService GetService<TService, TOptions>(PipelineServiceRequest2D<TService, TOptions> request, PipelineBuildContext2D context)
        where TService : PipelineSharedService2D
        where TOptions : notnull
    {
        (Type Service, string Kind, object Options) key = (typeof(TService), request.Kind, (object)request.Options);
        if (_services.TryGetValue(key, out (Type Factory, int Version, PipelineSharedService2D Value) existing))
        {
            if (existing.Factory != request.GetType() || existing.Version != request.Version)
            {
                throw new ArgumentException("Incompatible factories requested the same semantic service identity.");
            }

            return (TService)existing.Value;
        }

        if (!_creatingServices.Add(key))
        {
            throw new ArgumentException("Recursive shared service dependency.");
        }

        try
        {
            PipelineBuildContext2D callback = context.CreateCallbackContext();
            TService service;
            try
            {
                service = request.CreateInstance(callback) ?? throw new InvalidOperationException("Service factory returned null.");
            }
            finally
            {
                callback.Close();
            }

            service.Adopt(this);
            if (!ReferenceEquals(service, externalOwner) && _ownedIdentities.Add(service))
            {
                _owned.Add(service);
            }

            _services.Add(key, (request.GetType(), request.Version, service));
            return service;
        }
        finally
        {
            _creatingServices.Remove(key);
        }
    }

    internal void Commit() => _committed = true;

    internal object Resolve(PipelineOccurrence2D occurrence)
    {
        PipelineDeclaration2D declaration = occurrence.Declaration;
        if (_instances.TryGetValue(occurrence, out object? existing))
        {
            return existing;
        }

        if (!_creating.Add(occurrence))
        {
            throw new ArgumentException("Recursive pipeline declaration construction is not supported.");
        }

        try
        {
            var context = new PipelineBuildContext2D(device, this, occurrence);
            object result;
            try
            {
                result = declaration.CreateInstance(context);
            }
            finally
            {
                context.Close();
            }

            if (declaration.OwnsProcessor && result is PipelineSharedService2D service)
            {
                service.Adopt(this);
            }

            if (declaration.OwnsProcessor && result is PipelineElement2D element)
            {
                element.Adopt(declaration, this, occurrence);
            }

            if (declaration.OwnsProcessor && result is IDisposable owner && !ReferenceEquals(owner, externalOwner) && _ownedIdentities.Add(owner))
            {
                _owned.Add(owner);
            }

            declaration.ValidateInstance(result);
            _instances.Add(occurrence, result);
            return result;
        }
        finally
        {
            _creating.Remove(occurrence);
        }
    }
}
