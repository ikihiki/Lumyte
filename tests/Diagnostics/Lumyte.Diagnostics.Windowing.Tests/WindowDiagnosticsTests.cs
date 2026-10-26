using System.Numerics;
using Lumyte.Platform.Tests;

namespace Lumyte.Diagnostics.Windowing.Tests;

/// <summary>Checks scoped WindowSystem diagnostics and generated operations.</summary>
public sealed class WindowDiagnosticsTests
{
    private static readonly HashSet<DiagnosticPermission> _observe = [DiagnosticPermission.Observe];
    private static readonly HashSet<DiagnosticPermission> _edit = [DiagnosticPermission.Edit];
    private static readonly HashSet<DiagnosticPermission> _none = [];
    private static readonly Dictionary<string, DiagnosticValue> _noArguments = new(StringComparer.Ordinal);

    /// <summary>Checks the adapter joins an existing scoped diagnostic execution point.</summary>
    [Fact]
    public void RegistersScopedDiagnosticSubsystem()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(static options => options.Enabled = false);
        services.AddDiagnosticExecutionPoint<BeforeWindowUpdate>("window.before-update");
        services.AddWindowDiagnostics<BeforeWindowUpdate>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IDiagnosticPump<BeforeWindowUpdate> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeWindowUpdate>>();
        pump.Activate();

        DiagnosticSubsystemCatalog catalog = Assert.Single(pump.Catalog);
        Assert.Equal("window", catalog.Subsystem.Id);
        Assert.Contains(catalog.Operations, operation => operation.Id == "read");
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<WindowDiagnosticRegistry>(),
            scope.ServiceProvider.GetRequiredService<WindowDiagnosticRegistry>());
    }

    /// <summary>Checks native and hosted nodes are detached into scalar snapshots with stable diagnostic IDs.</summary>
    [Fact]
    public void ListsAndReadsManagedWindows()
    {
        using var platform = new TestPlatform();
        using var windows = new WindowSystem(platform);
        WindowNode root = windows.CreateNativeWindow(
            new WindowDescriptor { Title = "main", ClientSize = new Vector2(640, 480) },
            WindowRole.Normal);
        _ = windows.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = root,
            Bounds = new WindowRect(10, 20, 200, 100),
            Role = WindowRole.ContextMenu,
            Activation = WindowActivation.PreserveFocus,
        });
        var registry = new WindowDiagnosticRegistry();
        using WindowDiagnosticRegistration registration = registry.Register("main", windows);
        DiagnosticOperationSet operations = Operations(registry);

        Assert.Equal("main", Invoke(operations, "list-systems", _noArguments, _observe).Values!["systems"].String);
        DiagnosticOperationResult listed = Invoke(operations, "list-windows", Args(("system", "main")), _observe);
        Assert.Equal("window-1\nwindow-2", listed.Values!["windows"].String);

        DiagnosticOperationResult native = Invoke(operations, "read", Args(("system", "main"), ("window", "window-1")), _observe);
        Assert.Equal("Native", native.Values!["kind"].String);
        Assert.Equal("window-1", native.Values["presentation-root"].String);
        Assert.Equal(640, native.Values["client-width"].Double);
        Assert.NotEmpty(native.Values["native-window"].String!);

        DiagnosticOperationResult hosted = Invoke(operations, "read", Args(("system", "main"), ("window", "window-2")), _observe);
        Assert.Equal("Hosted", hosted.Values!["kind"].String);
        Assert.Equal("ContextMenu", hosted.Values["role"].String);
        Assert.Equal("window-1", hosted.Values["parent"].String);
        Assert.Equal(string.Empty, hosted.Values["native-window"].String);
        Assert.Equal(200, hosted.Values["width"].Double);
    }

    /// <summary>Checks permissions, management requests, removal, and lease-safe registration disposal.</summary>
    [Fact]
    public void ControlsAndRemovesManagedWindows()
    {
        using var platform = new TestPlatform();
        using var windows = new WindowSystem(platform);
        WindowNode root = windows.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        var registry = new WindowDiagnosticRegistry();
        WindowDiagnosticRegistration registration = registry.Register("main", windows);
        DiagnosticOperationSet operations = Operations(registry);
        Dictionary<string, DiagnosticValue> target = Args(("system", "main"), ("window", "window-1"));

        _ = Invoke(operations, "list-windows", Args(("system", "main")), _observe);
        Assert.Equal("forbidden", Invoke(operations, "show", target, _none).Code);
        DiagnosticOperationResult shown = Invoke(operations, "show", target, _edit);
        Assert.Equal("success", shown.Status);
        Assert.True(shown.Values!["request"].Int64 > 0);
        windows.Update();
        platform.PumpEvents();
        windows.Update();
        Assert.True(Invoke(operations, "read", target, _observe).Values!["is-visible"].Boolean);

        Assert.Equal("not-found", Invoke(operations, "read", Args(("system", "missing"), ("window", "window-1")), _observe).Code);
        Assert.Equal("not-found", Invoke(operations, "read", Args(("system", "main"), ("window", "missing")), _observe).Code);

        Assert.Equal("success", Invoke(operations, "close", target, _edit).Status);
        windows.Update();
        platform.PumpEvents();
        windows.Update();
        Assert.Equal(string.Empty, Invoke(operations, "list-windows", Args(("system", "main")), _observe).Values!["windows"].String);

        registration.Dispose();
        registration.Dispose();
        Assert.Equal(string.Empty, Invoke(operations, "list-systems", _noArguments, _observe).Values!["systems"].String);
        GC.KeepAlive(root);
    }

    /// <summary>Checks registration names, duplicates, and maximum system count.</summary>
    [Fact]
    public void BoundsRegistry()
    {
        using var platform = new TestPlatform();
        using var windows = new WindowSystem(platform);
        var registry = new WindowDiagnosticRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register("Invalid ID", windows));
        using WindowDiagnosticRegistration first = registry.Register("first", windows);
        Assert.Throws<ArgumentException>(() => registry.Register("first", windows));
    }

    private static DiagnosticOperationSet Operations(WindowDiagnosticRegistry registry)
        => new((IDiagnosticContributor)(object)new WindowDiagnostics(registry));

    private static Dictionary<string, DiagnosticValue> Args(params (string Name, string Value)[] values)
        => values.ToDictionary(value => value.Name, value => DiagnosticValue.From(value.Value), StringComparer.Ordinal);

    private static DiagnosticOperationResult Invoke(
        DiagnosticOperationSet operations,
        string operation,
        IReadOnlyDictionary<string, DiagnosticValue> arguments,
        IReadOnlySet<DiagnosticPermission> permissions)
        => operations.Invoke(operation, arguments, new(Guid.NewGuid(), Guid.NewGuid(), new(1, 1), "test", null, default), permissions);

    private sealed class BeforeWindowUpdate;
}
