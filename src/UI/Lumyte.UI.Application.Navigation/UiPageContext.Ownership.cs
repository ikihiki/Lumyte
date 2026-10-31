using Microsoft.Extensions.DependencyInjection;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Provides immutable borrowed services for one page activation.</summary>
public sealed partial class UiPageContext
{
    private readonly List<IDisposable> _owned = [];
    private bool _admitting;
    private bool _retired;
    private bool _disposed;

    internal Type[] ServiceTypes { get; set; } = [];

    /// <summary>Owns a manually created resource until activation cleanup, before its DI scope.</summary>
    /// <param name="resource">A unique resource created outside DI during Prepare or Build.</param>
    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!Activation.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Page resource ownership requires the activation dispatcher.");
        }

        if (!_admitting || _retired || _disposed)
        {
            throw new InvalidOperationException("Resources can only be owned during active page preparation or construction.");
        }

        if (resource is IServiceProvider or IServiceScope or IServiceScopeFactory || ServiceTypes.Any(type => type.IsInstanceOfType(resource)))
        {
            throw new ArgumentException("Registered service types belong to DI; Own accepts manually created resources only.", nameof(resource));
        }

        if (_owned.Any(current => ReferenceEquals(current, resource)))
        {
            throw new ArgumentException("The resource is already owned.", nameof(resource));
        }

        _owned.Add(resource);
    }

    internal void SetOwnershipAdmission(bool value) => _admitting = value;

    internal Widget Build(Func<Widget> build)
    {
        _admitting = true;
        try
        {
            return build() ?? throw new InvalidOperationException("The page returned no root.");
        }
        finally
        {
            _admitting = false;
        }
    }

    internal void Retire()
    {
        _retired = true;
        _admitting = false;
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Retire();
        List<Exception> errors = [];
        for (int index = _owned.Count - 1; index >= 0; index--)
        {
            try
            {
                _owned[index].Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        _owned.Clear();
        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }
    }
}
