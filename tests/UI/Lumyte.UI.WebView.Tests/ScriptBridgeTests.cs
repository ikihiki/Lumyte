using System.Buffers.Binary;
using System.Text;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Engine-free bounded scripting and origin security contracts.</summary>
public sealed class ScriptBridgeTests
{
    /// <summary>Only explicit exact HTTP origins are accepted.</summary>
    /// <param name="origin">The rejected origin.</param>
    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("file:///tmp/")]
    [InlineData("https://u:p@example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com/?q")]
    [InlineData("https://example.com/#f")]
    public void InvalidOriginsRejected(string origin) => Assert.Throws<ArgumentException>(() => CefMessageOrigins.Canonical(origin));

    /// <summary>Default ports normalize but dynamic loopback ports remain isolated.</summary>
    [Fact]
    public void ExactOriginAndDefaultDeny()
    {
        Assert.Equal("https://example.com", CefMessageOrigins.Canonical("https://EXAMPLE.com:443/"));
        Assert.False(CefMessageOrigins.Allows([], new("https://example.com/")));
        Assert.True(CefMessageOrigins.Allows(["http://127.0.0.1:3210"], new("http://127.0.0.1:3210/path")));
        Assert.False(CefMessageOrigins.Allows(["http://127.0.0.1:3210"], new("http://127.0.0.1:3211/path")));
    }

    /// <summary>Old epochs and late unknown replies cannot complete a different request.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CorrelationAndLateReplyAsync()
    {
        var requests = new CefScriptRequests();
        byte[] wire = [];
        Task<string> task = requests.EvaluateAsync("21*2", 5, TimeSpan.FromSeconds(1), default, bytes => wire = bytes);
        requests.Complete(Reply(wire, 4));
        Assert.False(task.IsCompleted);
        requests.Complete(Reply(wire));
        Assert.Equal("42", await task);
        requests.Complete(Reply(wire));
    }

    /// <summary>Cancellation and timeout free bounded pending slots without replay.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task TimeoutCancelAndCloseAsync()
    {
        var requests = new CefScriptRequests();
        int sends = 0;
        Task<string> timeout = requests.EvaluateAsync("1", 1, TimeSpan.FromMilliseconds(5), default, _ => sends++);
        Assert.Equal("cef-script-timeout", (await Assert.ThrowsAsync<UiWebViewScriptException>(() => timeout)).Code);
        using var cancel = new CancellationTokenSource();
        Task<string> cancelled = requests.EvaluateAsync("1", 1, TimeSpan.FromSeconds(1), cancel.Token, _ => sends++);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Task<string> close = requests.EvaluateAsync("1", 1, TimeSpan.FromSeconds(1), default, _ => sends++);
        requests.Abort("closed");
        Assert.Equal("closed", (await Assert.ThrowsAsync<UiWebViewScriptException>(() => close)).Code);
        Assert.Equal(3, sends);
    }

    /// <summary>Pending and UTF8 sizes are finite and malformed JSON fails closed.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task PendingLimitAndValidationAsync()
    {
        var requests = new CefScriptRequests();
        var tasks = new List<Task<string>>();
        byte[] wire = [];
        for (int i = 0; i < 16; i++)
        {
            tasks.Add(requests.EvaluateAsync("1", 1, TimeSpan.FromSeconds(1), default, b => wire = b));
        }

        await Assert.ThrowsAsync<UiWebViewScriptException>(() => requests.EvaluateAsync("1", 1, TimeSpan.FromSeconds(1), default, _ => { }));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => requests.Complete(Reply(wire, json: "not json")));
        requests.Abort("done");
        foreach (Task<string> task in tasks)
        {
            await Assert.ThrowsAsync<UiWebViewScriptException>(() => task);
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => requests.EvaluateAsync(new string('a', 3073), 1, TimeSpan.FromSeconds(1), default, _ => { }));
        await Assert.ThrowsAsync<EncoderFallbackException>(() => requests.EvaluateAsync("\ud800", 1, TimeSpan.FromSeconds(1), default, _ => { }));
    }

    /// <summary>Availability is negotiated rather than asserted by constructing the API.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task PublicApiRejectsUnavailableAndCreatingAsync()
    {
        var session = new UiWebViewSession(new("https://example.com"), () => { }, _ => { });
        Assert.False(session.ScriptAvailable);
        Assert.False(session.HostMessagesAvailable);
        Assert.False(session.ImeAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.EvaluateScriptAsync("1"));
        session.Update(UiWebViewState.Ready);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.EvaluateScriptAsync("1"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.EvaluateScriptAsync("1", TimeSpan.MaxValue));
    }

    /// <summary>Messages remain disabled until negotiated and callbacks execute on the checked owner.</summary>
    [Fact]
    public void MessageDeliveryChecksOwnerAndAvailability()
    {
        int checks = 0;
        var session = new UiWebViewSession(new("https://example.com"), () => checks++, _ => { });
        int events = 0;
        session.WebMessageReceived += (_, message) =>
        {
            Assert.Equal("https://example.com/", message.Source.AbsoluteUri);
            Assert.Equal("42", message.Json);
            Assert.True(message.IsMainFrame);
            events++;
        };
        session.Update(UiWebViewState.Ready);
        session.ReceiveMessage(new("https://example.com"), "42");
        Assert.Equal(0, events);
        session.HostMessagesAvailable = true;
        session.ReceiveMessage(new("https://example.com"), "42");
        Assert.Equal(1, events);
        Assert.True(checks >= 3);
        session.Update(UiWebViewState.Closing);
        session.ReceiveMessage(new("https://example.com"), "42");
        Assert.Equal(1, events);
    }

    /// <summary>Exception status is typed and retains bounded untrusted error text.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ExceptionResultIsTypedAsync()
    {
        var requests = new CefScriptRequests();
        byte[] request = [];
        Task<string> task = requests.EvaluateAsync("missing()", 1, TimeSpan.FromSeconds(1), default, body => request = body);
        requests.Complete(Reply(request, status: 1, json: "missing is not defined"));
        UiWebViewScriptException error = await Assert.ThrowsAsync<UiWebViewScriptException>(() => task);
        Assert.Equal("cef-script-exception", error.Code);
        Assert.Equal("missing is not defined", error.ScriptMessage);
    }

    /// <summary>Small valid deep results fail only their request, with later requests unaffected.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DeepJsonIsRequestLocalAsync()
    {
        string deep = new string('[', 65) + "0" + new string(']', 65);
        Assert.False(CefScriptRequests.ValidateJson(deep));
        Assert.True(CefScriptRequests.ValidateJson(new string('[', 64) + "0" + new string(']', 64)));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CefScriptRequests.ValidateJson(deep + "!"));
        var requests = new CefScriptRequests();
        byte[] first = [], second = [];
        Task<string> bad = requests.EvaluateAsync("deep", 1, TimeSpan.FromSeconds(5), default, bytes => first = bytes);
        Task<string> good = requests.EvaluateAsync("42", 1, TimeSpan.FromSeconds(5), default, bytes => second = bytes);
        requests.Complete(Reply(first, json: deep));
        Assert.Equal("cef-script-result-depth-limit", (await Assert.ThrowsAsync<UiWebViewScriptException>(() => bad)).Code);
        requests.Complete(Reply(second));
        Assert.Equal("42", await good);
        Task<string> native = requests.EvaluateAsync("deep", 1, TimeSpan.FromSeconds(5), default, bytes => first = bytes);
        requests.Complete(Reply(first, status: 4, json: "result-depth-limit"));
        Assert.Equal("cef-script-result-depth-limit", (await Assert.ThrowsAsync<UiWebViewScriptException>(() => native)).Code);
    }

    private static byte[] Reply(byte[] request, long? epoch = null, uint status = 0, string json = "42")
    {
        byte[] body = new byte[20 + Encoding.UTF8.GetByteCount(json)];
        request.AsSpan(0, 16).CopyTo(body);
        if (epoch is { } value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(8), value);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), status);
        Encoding.UTF8.GetBytes(json).CopyTo(body, 20);
        return body;
    }
}
