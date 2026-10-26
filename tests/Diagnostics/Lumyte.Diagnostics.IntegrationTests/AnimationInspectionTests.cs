using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks bounded Animation inspection and control validation.</summary>
public sealed class AnimationInspectionTests
{
    /// <summary>Checks the dedicated page only accepts the complete versioned contract.</summary>
    [Fact]
    public void RequiresCompleteAnimationSchema()
    {
        DiagnosticSubsystemCatalog catalog = Catalog();
        Assert.True(AnimationInspection.Supports(catalog));
        Assert.False(AnimationInspection.Supports(null));
        Assert.False(AnimationInspection.Supports(catalog with { Subsystem = catalog.Subsystem with { SchemaVersion = 2 } }));
        Assert.False(AnimationInspection.Supports(catalog with { Operations = catalog.Operations.Skip(1).ToArray() }));
        Assert.False(AnimationInspection.Supports(catalog with { Operations = [catalog.Operations[0] with { RequiredPermission = DiagnosticPermission.Edit }, .. catalog.Operations.Skip(1)] }));
        Assert.False(AnimationInspection.Supports(catalog with { Operations = [catalog.Operations[0] with { Results = Fields("targets:i") }, .. catalog.Operations.Skip(1)] }));
    }

    /// <summary>Checks target selection and control responses replace the displayed snapshot.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ListsSelectsAndControlsTargetsAsync()
    {
        var remote = new Remote();
        var model = new AnimationInspection(remote.InvokeAsync);
        await model.RefreshTargetsAsync(CancellationToken.None);
        Assert.Equal(new[] { "character.main", "character.motion" }, model.Targets);
        await model.SelectAsync("character.main", CancellationToken.None);
        Assert.Equal("playback", model.Selected!.Kind);
        await model.SeekAsync(42, CancellationToken.None);
        await model.SetSpeedAsync(1.5, CancellationToken.None);
        Assert.Equal(42, model.Selected.PositionTicks);
        Assert.Equal(1.5, model.Selected.Speed);
        Assert.Equal(new[] { "list", "read", "seek", "set-speed" }, remote.Calls.Select(call => call.OperationId));
        Assert.All(remote.Calls, call => Assert.Null(call.ExpectedRevision));
        Assert.Equal(remote.Calls.Count, remote.Calls.Select(call => call.RequestId).Distinct().Count());
    }

    /// <summary>Checks malformed lists, invalid local inputs and rejected operations are not retried.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task BoundsAndReportsRemoteDataAsync()
    {
        var remote = new Remote { Targets = "duplicate\nduplicate" };
        var model = new AnimationInspection(remote.InvokeAsync);
        await model.RefreshTargetsAsync(CancellationToken.None);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
        Assert.Empty(model.Targets);

        remote.Targets = "character.main";
        await model.RefreshTargetsAsync(CancellationToken.None);
        await model.SelectAsync("character.main", CancellationToken.None);
        int calls = remote.Calls.Count;
        await model.SeekAsync(-1, CancellationToken.None);
        await model.SetSpeedAsync(double.NaN, CancellationToken.None);
        await model.SetTriggerAsync("Invalid Trigger", CancellationToken.None);
        Assert.Equal(calls, remote.Calls.Count);
        Assert.Contains("Trigger", model.Error, StringComparison.Ordinal);

        remote.Rejection = "unsupported";
        await model.PlayAsync(CancellationToken.None);
        Assert.Equal(calls + 1, remote.Calls.Count);
        Assert.Contains("現在状態", model.Error, StringComparison.Ordinal);
    }

    private static DiagnosticSubsystemCatalog Catalog()
    {
        (string Id, DiagnosticPermission Permission, string Arguments, string Results)[] shapes =
        [
            ("list", DiagnosticPermission.Observe, string.Empty, "targets:s"),
            ("read", DiagnosticPermission.Observe, "id:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("play", DiagnosticPermission.Edit, "id:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("pause", DiagnosticPermission.Edit, "id:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("stop", DiagnosticPermission.Edit, "id:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("seek", DiagnosticPermission.Edit, "id:s position-ticks:i", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("set-speed", DiagnosticPermission.Edit, "id:s speed:d", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("set-trigger", DiagnosticPermission.Edit, "id:s trigger:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
            ("clear-triggers", DiagnosticPermission.Edit, "id:s", "id:s kind:s status:s state:s position-ticks:i speed:d"),
        ];
        return new(new(AnimationInspection.SubsystemId, "Animation", 1), shapes.Select(shape => new OperationDescriptor(shape.Id, shape.Id, shape.Permission, Fields(shape.Arguments), Fields(shape.Results))).ToArray());
    }

    private static DiagnosticField[] Fields(string shape) => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch { "i" => DiagnosticValueKind.Int64, "d" => DiagnosticValueKind.Double, _ => DiagnosticValueKind.String });
    }).ToArray();

    private sealed class Remote
    {
        public List<OperationInvocation> Calls { get; } = [];

        public string Targets { get; set; } = "character.main\ncharacter.motion";

        public string? Rejection { get; set; }

        private long Position { get; set; }

        private double Speed { get; set; } = 1;

        public Task<DiagnosticOperationResult> InvokeAsync(OperationInvocation invocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(invocation);
            if (Rejection is not null)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject(Rejection, "rejected"));
            }

            if (invocation.OperationId == "list")
            {
                return Task.FromResult(DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue> { ["targets"] = DiagnosticValue.From(Targets) }));
            }

            string id = invocation.Arguments["id"].String!;
            Position = invocation.OperationId == "seek" ? invocation.Arguments["position-ticks"].Int64 : Position;
            Speed = invocation.OperationId == "set-speed" ? invocation.Arguments["speed"].Double : Speed;
            return Task.FromResult(DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>
            {
                ["id"] = DiagnosticValue.From(id),
                ["kind"] = DiagnosticValue.From(id.EndsWith("motion", StringComparison.Ordinal) ? "state-machine" : "playback"),
                ["status"] = DiagnosticValue.From("Playing"),
                ["state"] = DiagnosticValue.From(id.EndsWith("motion", StringComparison.Ordinal) ? "Idle" : string.Empty),
                ["position-ticks"] = DiagnosticValue.From(Position),
                ["speed"] = DiagnosticValue.From(Speed),
            }));
        }
    }
}
