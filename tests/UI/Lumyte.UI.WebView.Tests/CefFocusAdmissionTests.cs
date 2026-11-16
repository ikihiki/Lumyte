using System.Reflection;
using System.Runtime.CompilerServices;
using Lumyte.UI.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Fake native-host callbacks exercise the actual engine work focus boundary, not CEF.</summary>
public sealed class CefFocusAdmissionTests
{
    /// <summary>Focus queued before browser creation is applied once a host becomes available.</summary>
    /// <param name="latest">The latest requested focus.</param>
    /// <param name="closing">Whether closure supersedes focus.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CreationAppliesLatestFocusThroughFakeHost(bool latest, bool closing)
    {
        // No constructors, runtime initialization, native CEF browser or P/Invoke are used.
        var session = (CefEngineSession)RuntimeHelpers.GetUninitializedObject(typeof(CefEngineSession));
        Set(session, "_gate", Activator.CreateInstance(typeof(Lock))!);
        var admission = new CefFocusAdmission();
        admission.Set(!latest, 1);
        admission.Set(latest, 2);

        Set(session, "_closing", closing);
        bool? applied = null;
        int cancelled = 0;
        MethodInfo apply = typeof(CefEngineSession).GetMethod("ApplyPendingFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(apply);
        Set(session, "_focusAdmission", admission);
        apply.Invoke(session, [new Action<bool>(value => applied = value), new Action(() => cancelled++)]);
        Assert.Equal(!closing && latest, applied);
        Assert.Equal(latest && !closing ? 0 : 1, cancelled);
        Assert.Equal(2, admission.Request);
    }

    /// <summary>Old focus generations cannot replace the latest intent or duplicate native calls.</summary>
    [Fact]
    public void FakeHostIgnoresStaleFocusAndRepeatedWork()
    {
        var admission = new CefFocusAdmission();
        admission.Set(true, 2);
        admission.Set(false, 1);
        var calls = new List<bool>();
        int cancelled = 0;
        admission.Apply(false, calls.Add, () => cancelled++);
        admission.Apply(false, calls.Add, () => cancelled++);
        admission.Apply(true, calls.Add, () => cancelled++);
        Assert.Equal(new[] { true, false }, calls);
        Assert.Equal(1, cancelled);
        Assert.Equal(2, admission.Request);
    }

    private static void Set(CefEngineSession session, string name, object value) => typeof(CefEngineSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
}
