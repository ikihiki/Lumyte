using System.Reflection;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Browser upload faults must not escape through the UI participant.</summary>
public sealed class UploadFailureIsolationTests
{
    /// <summary>Completed body and popup upload errors fault only their borrowed browser.</summary>
    /// <param name="popup">Whether the failed operation is the popup upload.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedUploadFailureDoesNotEscapeAsync(bool popup)
    {
        var budget = new WebViewPixelBudget(1048576);
        using var helper = new CefHelperSession(new("https://example.test/"), budget, () => { }, new());
        var session = new UiWebViewSession(new("https://example.test/"), () => { }, _ => { });
        Type entryType = typeof(CefWebViewParticipant).GetNestedType("Entry", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(entryType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [null, session, helper, budget, new WebViewPixelBudget(1048576)], null)!;
        entryType.GetField(popup ? "_popupUpload" : "_upload", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, Task.FromException<Image2D?>(new InvalidOperationException("webview-pixel-budget-exceeded")));
        MethodInfo advance = entryType.GetMethod("AdvanceUpload", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Exception? error = Record.Exception(() => advance.Invoke(entry, [true]));
        Assert.Null(error);
        Assert.Equal(UiWebViewState.Faulted, session.State);
        Assert.Equal("cef-upload-failed", session.FailureCode);
        await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
