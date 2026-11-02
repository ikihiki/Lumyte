using System.Numerics;
using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Collects UI configuration without creating native resources or resolving services.</summary>
public sealed class UiApplicationBuilder
{
    private readonly string[] _args;
    private readonly ServiceCollection _services = new();
    private UiApplicationOptions _options = new();
    private Func<UiApplicationDriver>? _driver;
    private Func<UiApplicationContext, IReadOnlyList<UiWindowDeclaration>>? _windows;
    private Func<UiWindowManagerContext, IUiWindowManager>? _manager;
    private Func<IServiceProvider, IUiTextLayoutProvider>? _text;
    private string _primaryKey = "main";
    private bool _built;
    private string _defaultTitle = "Lumyte";
    private Vector2 _defaultSize = new(800, 600);

    internal UiApplicationBuilder(string[]? args)
    {
        _args = args?.ToArray() ?? [];
    }

    /// <summary>Gets registrations, made read-only by Build.</summary>
    public IServiceCollection Services => _services;

    internal List<Func<IServiceProvider, IUiApplicationParticipant>> Participants { get; } = [];

    /// <summary>Sets validated application budgets.</summary>
    /// <param name="options">The immutable options.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder Configure(UiApplicationOptions options)
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        return this;
    }

    /// <summary>Installs an application-owned manager using borrowed defaults.</summary>
    /// <param name="factory">The owner-thread factory.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder UseWindowManager(Func<UiWindowManagerContext, IUiWindowManager> factory)
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(factory);
        if (_manager is not null)
        {
            throw new ArgumentException("A window manager was already configured.", nameof(factory));
        }

        _manager = factory;
        return this;
    }

    /// <summary>Configures one primary root without requiring pages or navigation.</summary>
    /// <param name="factory">The root factory.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder UseRoot(Func<UiApplicationWindowContext, Widget> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return UseWindows(_ => [Compose.Window("main", _defaultTitle, root: factory, initialClientSize: _defaultSize)]);
    }

    /// <summary>Transfers ownership of newly created content to the application.</summary>
    /// <param name="factory">A factory; do not return a DI-owned disposable service.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder UseContent(Func<IServiceProvider, IUiApplicationContent> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return UseWindows(_ => [Compose.Window("main", _defaultTitle, content: factory, initialClientSize: _defaultSize)]);
    }

    /// <summary>Defines the passive window list, rebuilt only on request.</summary>
    /// <param name="build">The owner-thread declaration factory.</param>
    /// <param name="primaryKey">The always-open unowned primary declaration.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder UseWindows(Func<UiApplicationContext, IReadOnlyList<UiWindowDeclaration>> build, string primaryKey = "main")
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKey);
        if (_windows is not null)
        {
            throw new ArgumentException("Application content was already configured.", nameof(build));
        }

        _windows = build;
        _primaryKey = primaryKey;
        return this;
    }

    /// <summary>Borrows a font-configured layout provider; the selected driver owns its OS text service.</summary>
    /// <param name="provider">The provider factory.</param>
    /// <returns>This builder.</returns>
    public UiApplicationBuilder UseTextInput(Func<IServiceProvider, IUiTextLayoutProvider> provider)
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(provider);
        if (_text is not null)
        {
            throw new ArgumentException("Text input was already configured.", nameof(provider));
        }

        _text = provider;
        return this;
    }

    /// <summary>Freezes configuration and validates DI without initializing the driver.</summary>
    /// <returns>The unstarted application.</returns>
    public UiApplication Build()
    {
        CheckMutable();
        if (_driver is null || _windows is null)
        {
            throw new InvalidOperationException("Configure one driver and one content source before Build.");
        }

        _options.Validate();
        if (_services.Any(descriptor => descriptor.ImplementationInstance is Lumyte.Resources.ResourceManager or Lumyte.Resources.Execution.ResourceRuntime or Lumyte.Execution.ExecutionRuntime))
        {
            throw new UiApplicationException("unsupported-execution-module");
        }

        var dispatcher = new UiApplicationDispatcher(_options);
        if (_services.Any(descriptor => descriptor.ServiceType == typeof(IUiDispatcher)))
        {
            throw new ArgumentException("IUiDispatcher is owned by the application.");
        }

        IServiceCollection registrations = new ServiceCollection();
        foreach (ServiceDescriptor descriptor in _services)
        {
            registrations.Add(descriptor);
        }

        registrations.AddSingleton<IUiDispatcher>(dispatcher);
        registrations.AddSingleton(_options.TimeProvider);
        ServiceProvider provider = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _services.MakeReadOnly();
        _built = true;
        return new(_args, _options, dispatcher, provider, _driver, _windows, _primaryKey, _manager, _text, Participants.ToArray());
    }

    internal static void ValidateSize(Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X <= 0 || value.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    internal UiApplicationBuilder UseDriver(Func<UiApplicationDriver> factory)
    {
        CheckMutable();
        if (_driver is not null)
        {
            throw new ArgumentException("A driver was already configured.", nameof(factory));
        }

        _driver = factory;
        return this;
    }

    internal void SetPrimaryDefaults(string title, Vector2 size)
    {
        CheckMutable();
        _defaultTitle = title;
        _defaultSize = size;
    }

    internal void CheckMutable()
    {
        if (_built)
        {
            throw new InvalidOperationException("The builder is frozen.");
        }
    }
}
