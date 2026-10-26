using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Xunit;

#pragma warning disable SA1117, SA1118 // Keep protocol fixtures aligned with their wire shapes.

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks bounded Window inspection and control validation.</summary>
public sealed class WindowInspectionTests
{
    /// <summary>Checks the dedicated page only accepts the complete versioned contract.</summary>
    [Fact]
    public void RequiresCompleteWindowSchema()
    {
        DiagnosticSubsystemCatalog catalog = Catalog();
        Assert.True(WindowInspection.Supports(catalog));
        Assert.False(WindowInspection.Supports(null));
        Assert.False(WindowInspection.Supports(catalog with { Subsystem = catalog.Subsystem with { SchemaVersion = 2 } }));
        Assert.False(WindowInspection.Supports(catalog with { Operations = catalog.Operations.Skip(1).ToArray() }));
        Assert.False(WindowInspection.Supports(catalog with { Operations = [catalog.Operations[0] with { RequiredPermission = DiagnosticPermission.Edit }, .. catalog.Operations.Skip(1)] }));
        Assert.False(WindowInspection.Supports(catalog with { Operations = [catalog.Operations[0] with { Results = Fields("systems:i") }, .. catalog.Operations.Skip(1)] }));
    }

    /// <summary>Checks system and window selection, scalar snapshots, and command receipts.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ListsSelectsAndControlsWindowsAsync()
    {
        var remote = new Remote();
        var model = new WindowInspection(remote.InvokeAsync);
        await model.RefreshSystemsAsync(CancellationToken.None);
        Assert.Equal(new[] { "main", "tools" }, model.Systems);
        await model.SelectSystemAsync("main", CancellationToken.None);
        Assert.Equal(new[] { "window-1", "window-2" }, model.Windows);
        await model.SelectWindowAsync("window-2", CancellationToken.None);
        Assert.Equal("Hosted", model.Selected!.Kind);
        Assert.Equal("window-1", model.Selected.Parent);

        await model.ShowAsync(CancellationToken.None);
        Assert.Contains("要求 7", model.Notice, StringComparison.Ordinal);
        Assert.Equal(new[] { "list-systems", "list-windows", "read", "show" }, remote.Calls.Select(call => call.OperationId));
        Assert.All(remote.Calls, call => Assert.Null(call.ExpectedRevision));
    }

    /// <summary>Checks malformed lists, malformed snapshots, and rejected operations are contained.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task BoundsAndReportsRemoteDataAsync()
    {
        var remote = new Remote { Systems = "duplicate\nduplicate" };
        var model = new WindowInspection(remote.InvokeAsync);
        await model.RefreshSystemsAsync(CancellationToken.None);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
        Assert.Empty(model.Systems);

        remote.Systems = "main";
        await model.RefreshSystemsAsync(CancellationToken.None);
        await model.SelectSystemAsync("main", CancellationToken.None);
        remote.InvalidSnapshot = true;
        await model.SelectWindowAsync("window-1", CancellationToken.None);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);

        remote.InvalidSnapshot = false;
        await model.SelectWindowAsync("window-1", CancellationToken.None);
        remote.Rejection = "invalid-state";
        await model.CloseAsync(CancellationToken.None);
        Assert.Contains("現在のWindow状態", model.Error, StringComparison.Ordinal);
    }

    private static DiagnosticSubsystemCatalog Catalog()
    {
        const string SnapshotShape = "system:s window:s parent:s presentation-root:s kind:s role:s ownership:s native-window:s x:d y:d width:d height:d client-width:d client-height:d pixel-width:i pixel-height:i scale-x:d scale-y:d is-visible:b is-effectively-visible:b lifetime:s";
        (string Id, DiagnosticPermission Permission, string Arguments, string Results)[] shapes =
        [
            ("list-systems", DiagnosticPermission.Observe, string.Empty, "systems:s"),
            ("list-windows", DiagnosticPermission.Observe, "system:s", "windows:s"),
            ("read", DiagnosticPermission.Observe, "system:s window:s", SnapshotShape),
            ("show", DiagnosticPermission.Edit, "system:s window:s", "system:s window:s request:i"),
            ("hide", DiagnosticPermission.Edit, "system:s window:s", "system:s window:s request:i"),
            ("activate", DiagnosticPermission.Edit, "system:s window:s", "system:s window:s request:i"),
            ("bring-to-front", DiagnosticPermission.Edit, "system:s window:s", "system:s window:s request:i"),
            ("close", DiagnosticPermission.Edit, "system:s window:s", "system:s window:s request:i"),
        ];
        return new(new(WindowInspection.SubsystemId, "Window", 1), shapes.Select(shape => new OperationDescriptor(shape.Id, shape.Id, shape.Permission, Fields(shape.Arguments), Fields(shape.Results))).ToArray());
    }

    private static DiagnosticField[] Fields(string shape) => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch
        {
            "b" => DiagnosticValueKind.Boolean,
            "i" => DiagnosticValueKind.Int64,
            "d" => DiagnosticValueKind.Double,
            _ => DiagnosticValueKind.String,
        });
    }).ToArray();

    private sealed class Remote
    {
        public List<OperationInvocation> Calls { get; } = [];

        public string Systems { get; set; } = "main\ntools";

        public bool InvalidSnapshot { get; set; }

        public string? Rejection { get; set; }

        public Task<DiagnosticOperationResult> InvokeAsync(OperationInvocation invocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(invocation);
            if (Rejection is not null)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject(Rejection, "rejected"));
            }

            if (invocation.OperationId == "list-systems")
            {
                return Result(("systems", DiagnosticValue.From(Systems)));
            }

            if (invocation.OperationId == "list-windows")
            {
                return Result(("windows", DiagnosticValue.From("window-1\nwindow-2")));
            }

            string system = invocation.Arguments["system"].String!;
            string window = invocation.Arguments["window"].String!;
            if (invocation.OperationId != "read")
            {
                return Result(("system", DiagnosticValue.From(system)), ("window", DiagnosticValue.From(window)), ("request", DiagnosticValue.From(7L)));
            }

            bool hosted = window == "window-2";
            return Result(
                ("system", DiagnosticValue.From(system)), ("window", DiagnosticValue.From(window)), ("parent", DiagnosticValue.From(hosted ? "window-1" : string.Empty)),
                ("presentation-root", DiagnosticValue.From("window-1")), ("kind", DiagnosticValue.From(InvalidSnapshot ? "Unknown" : hosted ? "Hosted" : "Native")),
                ("role", DiagnosticValue.From(hosted ? "ContextMenu" : "Normal")), ("ownership", DiagnosticValue.From("Owned")),
                ("native-window", DiagnosticValue.From(hosted ? string.Empty : "00000000-0000-0000-0000-000000000001:1")),
                ("x", DiagnosticValue.From(hosted ? -10.0 : 0.0)), ("y", DiagnosticValue.From(0.0)), ("width", DiagnosticValue.From(200.0)), ("height", DiagnosticValue.From(100.0)),
                ("client-width", DiagnosticValue.From(200.0)), ("client-height", DiagnosticValue.From(100.0)), ("pixel-width", DiagnosticValue.From(200L)), ("pixel-height", DiagnosticValue.From(100L)),
                ("scale-x", DiagnosticValue.From(1.0)), ("scale-y", DiagnosticValue.From(1.0)), ("is-visible", DiagnosticValue.From(true)),
                ("is-effectively-visible", DiagnosticValue.From(true)), ("lifetime", DiagnosticValue.From("Open")));
        }

        private static Task<DiagnosticOperationResult> Result(params (string Name, DiagnosticValue Value)[] fields)
            => Task.FromResult(DiagnosticOperationResult.Success(fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal)));
    }
}
