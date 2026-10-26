using System.Buffers;
using System.Text.Json;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks the boundaries between collection, public results and transport implementations.</summary>
public sealed class ApiContractTests
{
    /// <summary>Checks a full structured exception log is accepted by both wire representations.</summary>
    [Fact]
    public void CollectedExceptionLogFitsBothTransports()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = true;
            options.AllowedLogCategoryPrefixes = ["Lumyte.Contract"];
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        DiagnosticTelemetry telemetry = scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        telemetry.Start();
        string instance = scope.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D");
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Contract");
        var fields = Enumerable.Range(0, DiagnosticTelemetryLimits.MaximumUserFields).ToDictionary(index => $"field-{index}", index => (object?)index);
        using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = instance }))
        {
            logger.Log(LogLevel.Warning, new EventId(1), fields, new InvalidOperationException("Failure"), static (_, _) => "Collected exception");
        }

        Assert.True(telemetry.TryRead(out DiagnosticEvent? item));
        Assert.Equal(DiagnosticTelemetryLimits.MaximumEventFields, item.Fields.Count);
        var message = new DiagnosticMessage(Guid.NewGuid(), Guid.NewGuid(), DiagnosticMessageKind.Telemetry, null, null, [item]);
        var json = new ArrayBufferWriter<byte>();
        DiagnosticJsonMessageEncoder.Write(json, message);
        DiagnosticMessage decodedJson = JsonSerializer.Deserialize(json.WrittenSpan, DiagnosticJson.Context.DiagnosticMessage)!;
        DiagnosticProtocol.Validate(decodedJson);
        DiagnosticMessage decodedMessagePack = MessagePackSerializer.Deserialize<DiagnosticMessage>(MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options), DiagnosticMessagePack.Options);
        DiagnosticProtocol.Validate(decodedMessagePack);
        Assert.Equal(item.Fields.Count, Assert.Single(decodedJson.Events).Fields.Count);
        Assert.Equal(item.Fields.Count, Assert.Single(decodedMessagePack.Events).Fields.Count);
        Assert.Equal("Failure", Assert.Single(decodedMessagePack.Events).Fields["exception.message"].String);

        var tooMany = item.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        tooMany.Add("extra", DiagnosticValue.From(true));
        DiagnosticMessage oversized = message with { Events = [item with { Fields = tooMany }] };
        Assert.Throws<ArgumentException>(() => DiagnosticProtocol.Validate(oversized));
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DiagnosticMessage>(MessagePackSerializer.Serialize(oversized, DiagnosticMessagePack.Options), DiagnosticMessagePack.Options));
    }

    /// <summary>Checks typed outcomes leave the JSON wire contract unchanged.</summary>
    /// <param name="status">The original wire status.</param>
    /// <param name="expected">The typed interpretation.</param>
    [Theory]
    [InlineData("success", DiagnosticOperationStatus.Success)]
    [InlineData("rejected", DiagnosticOperationStatus.Rejected)]
    [InlineData("conflict", DiagnosticOperationStatus.Conflict)]
    [InlineData("future-status", DiagnosticOperationStatus.Unknown)]
    public void TypedOutcomePreservesWireStatus(string status, DiagnosticOperationStatus expected)
    {
        var result = new DiagnosticOperationResult(status);
        Assert.Equal(expected, result.GetStatus());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, DiagnosticJson.Context.DiagnosticOperationResult));
        Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, json.RootElement.EnumerateObject().Count());
    }

    /// <summary>Checks malformed responses consistently use the common transport exception.</summary>
    /// <param name="publication">Whether the malformed response is a publication receipt.</param>
    /// <returns>The asynchronous transport check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedHttpResponsesUseTransportExceptionAsync(bool publication)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication app = builder.Build();
        var welcome = new SessionWelcome(Guid.NewGuid(), "secret", [DiagnosticPermission.Observe]);
        app.MapPost("/diagnostics/v1/sessions", () => Results.Json(welcome, DiagnosticJson.Context.SessionWelcome));
        app.MapGet("/diagnostics/v1/sessions/{id}/commands", () => Results.Text("{", "application/json"));
        app.MapPost("/diagnostics/v1/sessions/{id}/messages", () => Results.Text("{", "application/json"));
        app.MapDelete("/diagnostics/v1/sessions/{id}", () => Results.NoContent());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);
        var services = new ServiceCollection();
        services.AddHttpDiagnosticTransport(options =>
        {
            options.BaseAddress = new Uri(app.Urls.Single());
            options.GameToken = "token";
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        IDiagnosticTransportFactory factory = provider.GetRequiredService<IDiagnosticTransportFactory>();
        await using IDiagnosticConnection connection = await factory.OpenAsync(new(Guid.NewGuid(), 1, []), timeout.Token);
        DiagnosticTransportException error;
        if (publication)
        {
            error = await Assert.ThrowsAsync<DiagnosticTransportException>(() => connection.PublishAsync(new(Guid.NewGuid(), welcome.SessionId, DiagnosticMessageKind.Heartbeat, null, null, []), timeout.Token).AsTask());
        }
        else
        {
            await using IAsyncEnumerator<DiagnosticCommand> commands = connection.ReadCommandsAsync(timeout.Token).GetAsyncEnumerator();
            error = await Assert.ThrowsAsync<DiagnosticTransportException>(() => commands.MoveNextAsync().AsTask());
        }

        Assert.IsType<JsonException>(error.InnerException);
    }
}
