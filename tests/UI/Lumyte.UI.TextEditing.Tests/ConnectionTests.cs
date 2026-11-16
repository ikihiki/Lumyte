using System.Globalization;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Signals;
using Lumyte.TextEditing;
using Lumyte.UI.Automation;
using Lumyte.UI.SampleSupport;
using Lumyte.UI.TextEditing.Automation;
using Lumyte.UI.TextEditing.Parley;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.Tests;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Defines connection tests.</summary>
public sealed class ConnectionTests
{
    /// <summary>Numeric Fill uses the actual document, borrowed provider and one existing editing lease.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task NumericFillRetainsInvalidBufferAndAcceptsValidLocaleAsync()
    {
        var declaration = new Compose.Definitions.NumericBox { Value = 2, Min = -100, Max = 100, Culture = CultureInfo.GetCultureInfo("fr-FR") };
        var values = new List<double?>();
        declaration.OnChanged = values.Add;
        using var fixture = new Fixture(numeric: declaration);
        NumericBoxElement numeric = Assert.IsType<NumericBoxElement>(fixture.Editor);
        await using var host = new UiSampleAutomationHost(fixture.Surface, _ =>
        {
            fixture.Pump();
            return ValueTask.CompletedTask;
        });
        var page = new UiAutomationPage(new UiTextAutomationHost(host, fixture.Connection, allowTextInspection: true));
        UiLocator editor = page.GetByTestId("editor");
        await editor.FillAsync("-");
        fixture.Pump();
        Assert.Equal("-", fixture.Document.CaptureSnapshot().CommittedText);
        Assert.Equal(2, numeric.Value);
        Assert.False(numeric.IsValid);
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes[0].Numeric!.IsValid);
        await editor.FillAsync("12,5");
        fixture.Pump();
        Assert.Equal(12.5, numeric.Value);
        Assert.True(numeric.IsValid);
        Assert.Equal(new double?[] { 12.5 }, values);
        Assert.NotNull(numeric.CurrentLayout);
        Assert.Equal(2, fixture.Service.Acquisitions);
        Assert.Equal(0, fixture.Service.Gates);
        Assert.Single(fixture.Surface.CaptureAutomationSnapshot().Nodes);
        Assert.Equal(UiRole.TextBox, fixture.Surface.CaptureAutomationSnapshot().Nodes[0].Role);
        Assert.Null(fixture.Surface.CaptureAutomationSnapshot().Nodes[0].Value);
    }

    /// <summary>Incomplete numeric forms and nonfinite/out-of-range inputs do not alter the accepted value.</summary>
    /// <param name="text">The actual edited text.</param>
    [Theory]
    [InlineData("+")]
    [InlineData("1.")]
    [InlineData("1e")]
    [InlineData("1e-")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("101")]
    [InlineData("1,000")]
    public void NumericIncompleteAndInvalidTextRetainsAcceptedValue(string text)
    {
        using var fixture = new Fixture(numeric: new() { Value = 5, Min = -100, Max = 100 });
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        Assert.True(TextEditingCommands.ReplaceAll(fixture.Document, text, TextEditOrigin.Keyboard));
        fixture.Pump();
        NumericBoxElement numeric = Assert.IsType<NumericBoxElement>(fixture.Editor);
        Assert.Equal(5, numeric.Value);
        Assert.False(numeric.IsValid);
        Assert.Equal(text, fixture.Document.CaptureSnapshot().CommittedText);
        fixture.Surface.Dispatch(new UiKeyInput(Key.Enter, true));
        Assert.Equal(text, fixture.Document.CaptureSnapshot().CommittedText);
        fixture.Surface.Dispatch(new UiKeyInput(Key.Escape, true));
        Assert.Equal("5", fixture.Document.CaptureSnapshot().CommittedText);
        Assert.True(numeric.IsValid);
    }

    /// <summary>Enter/focus commit once, empty is null, and declaration adoption is silent.</summary>
    [Fact]
    public void NumericCommitEmptyAndSilentAdoption()
    {
        int commits = 0;
        int changes = 0;
        using var fixture = new Fixture(numeric: new() { Value = 1, OnChanged = _ => changes++, OnCommitted = _ => commits++ });
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextEditingCommands.ReplaceAll(fixture.Document, "2e0", TextEditOrigin.Keyboard);
        fixture.Pump();
        fixture.Surface.Dispatch(new UiKeyInput(Key.Enter, true));
        fixture.Surface.Dispatch(new UiKeyInput(Key.Enter, false));
        fixture.Pump();
        Assert.Equal("2", fixture.Document.CaptureSnapshot().CommittedText);
        Assert.Equal(1, commits);
        Assert.Equal(1, changes);
        fixture.Surface.TryFocus(null);
        fixture.Pump();
        Assert.Equal(1, commits);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextEditingCommands.ReplaceAll(fixture.Document, string.Empty, TextEditOrigin.Keyboard);
        fixture.Pump();
        Assert.Null(((NumericBoxElement)fixture.Editor).Value);
        fixture.Surface.TryFocus(null);
        Assert.Equal(2, commits);
        fixture.Surface.Rebuild(Compose.NumericBox(value: 1, layoutProvider: fixture.Provider, testId: "editor"));
        fixture.Pump();
        Assert.Equal(1, ((NumericBoxElement)fixture.Editor).Value);
        Assert.Equal("1", fixture.Document.CaptureSnapshot().CommittedText);
        Assert.Equal(2, changes);
    }

    /// <summary>Preedit never updates numeric state and an external value cancels it through the existing session.</summary>
    [Fact]
    public void NumericPreeditAndExternalAdoptionUseSameEditorSession()
    {
        using var fixture = new Fixture(numeric: new() { Value = 5 });
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextDocument document = fixture.Document;
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 1), []));
            edit.Replace(new(0, 1), "77", new(2, 2));
            edit.Commit();
        }

        fixture.Pump();
        Assert.Equal(5, ((NumericBoxElement)fixture.Editor).Value);
        fixture.Surface.Dispatch(new UiKeyInput(Key.Enter, true));
        Assert.NotNull(document.CaptureSnapshot().Composition);
        fixture.Surface.Rebuild(Compose.NumericBox(value: 9, layoutProvider: fixture.Provider, testId: "editor"));
        fixture.Pump();
        Assert.Same(document, fixture.Document);
        Assert.Null(document.CaptureSnapshot().Composition);
        Assert.Equal("9", document.CaptureSnapshot().CommittedText);
        Assert.Equal(9, ((NumericBoxElement)fixture.Editor).Value);
    }

    /// <summary>Numeric Signal declarations remain one-way and adopt external values without sharing a document.</summary>
    [Fact]
    public void NumericSignalDoesNotImplicitlyWriteBackAndAdoptsExternalValue()
    {
        var model = Signal.Create<double?>(1);
        using var fixture = new Fixture(numeric: new() { Value = 1 });
        fixture.Surface.Rebuild(Compose.NumericBox(value: model, layoutProvider: fixture.Provider, testId: "editor"));
        fixture.Pump();
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextDocument document = fixture.Document;
        TextEditingCommands.ReplaceAll(document, "2", TextEditOrigin.Keyboard);
        fixture.Pump();
        Assert.Equal(2, ((NumericBoxElement)fixture.Editor).Value);
        Assert.Equal(1, model.Value);
        model.Value = 9;
        fixture.Pump();
        Assert.Equal(9, ((NumericBoxElement)fixture.Editor).Value);
        Assert.Equal("9", document.CaptureSnapshot().CommittedText);
        Assert.Same(document, fixture.Document);
    }

    /// <summary>Publication-driven disable/removal is cancellation, not a user editor commit.</summary>
    /// <param name="remove">Whether to remove instead of disable the numeric editor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericPublicationCancellationNeverCommitsOldBuffer(bool remove)
    {
        int commits = 0;
        using var fixture = new Fixture(numeric: new() { Value = 1, OnCommitted = _ => commits++ });
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextEditingCommands.ReplaceAll(fixture.Document, "2", TextEditOrigin.Keyboard);
        fixture.Pump();
        Assert.Equal(2, ((NumericBoxElement)fixture.Editor).Value);
        fixture.Surface.Rebuild(remove ? Lumyte.UI.Compose.Rectangle() : Compose.NumericBox(value: 9, isEnabled: false, layoutProvider: fixture.Provider, testId: "editor", onCommitted: _ => commits++));
        fixture.Surface.UpdateTree();
        Assert.Equal(0, commits);
        if (!remove)
        {
            Assert.Equal(9, ((NumericBoxElement)fixture.Editor).Value);
        }
    }

    /// <summary>Rebuilding from a change callback never commits a replacement declaration's value.</summary>
    [Fact]
    public void NumericCommitCallbackReentryCannotCommitReplacement()
    {
        int commits = 0;
        using var fixture = new Fixture(numeric: new() { Value = 1 });
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        ((Compose.Definitions.NumericBox)fixture.Editor.Widget).OnChanged = _ =>
        {
            fixture.Surface.Rebuild(Compose.NumericBox(value: 9, layoutProvider: fixture.Provider, testId: "editor", onCommitted: _ => commits++));
            fixture.Surface.UpdateTree();
        };
        TextEditingCommands.ReplaceAll(fixture.Document, "2", TextEditOrigin.Keyboard);
        fixture.Surface.Dispatch(new UiKeyInput(Key.Enter, true));
        Assert.Equal(9, ((NumericBoxElement)fixture.Editor).Value);
        Assert.Equal(0, commits);
    }

    /// <summary>Culture is immutable and shared numeric declarations own separate actual editor documents.</summary>
    [Fact]
    public void NumericCultureAndDocumentOwnershipAreExplicit()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        Compose.Definitions.NumericBox declaration = Compose.NumericBox(value: 1.5, culture: culture);
        culture.NumberFormat.NumberDecimalSeparator = ",";
        Assert.Equal(".", declaration.Culture.NumberFormat.NumberDecimalSeparator);
        Assert.True(declaration.Culture.IsReadOnly);
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        var a = (NumericBoxElement)first.RootElement;
        var b = (NumericBoxElement)second.RootElement;
        Assert.NotSame(a.Document, b.Document);
        Assert.Equal("1.5", a.Document.CaptureSnapshot().CommittedText);
        TextEditingCommands.ReplaceAll(a.Document, "2", TextEditOrigin.Keyboard);
        Assert.Equal("1.5", b.Document.CaptureSnapshot().CommittedText);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            first.Rebuild(Compose.NumericBox(value: double.NaN));
            first.UpdateTree();
        });
    }

    /// <summary>Checks a real editor publishes the same nonzero scroll translation as input geometry.</summary>
    [Fact]
    public void ScrolledEditorPublishesTranslatedImeCaret()
    {
        using var fixture = new Fixture(scrolled: true);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextInputGeometry before = fixture.Connection.Geometry!;
        fixture.Scroll.ScrollTo(40);
        fixture.Pump();
        TextInputGeometry after = fixture.Connection.Geometry!;
        Assert.Equal(before.Caret.Y - 40, after.Caret.Y);
        Assert.Equal(before.Caret.X, after.Caret.X);
        Assert.Same(fixture.Editor, fixture.Surface.FocusedElement);
        Assert.Equal(-40, fixture.Surface.RootElement.Children[0].Bounds.Y);
    }

    /// <summary>Checks touch editing capture is not stolen by an ancestor viewport.</summary>
    [Fact]
    public void EditorTouchSelectionPreservesViewportOffset()
    {
        using var fixture = new Fixture(scrolled: true);
        fixture.Surface.Dispatch(new UiPointerInput(8, UiPointerPhase.Down, new(5, 5)) { Device = UiPointerDevice.Touch });
        fixture.Surface.Dispatch(new UiPointerInput(8, UiPointerPhase.Move, new(5, -30)) { Device = UiPointerDevice.Touch });
        fixture.Surface.Dispatch(new UiPointerInput(8, UiPointerPhase.Up, new(5, -30)) { Device = UiPointerDevice.Touch });
        Assert.Equal(0, fixture.Scroll.Offset);
        Assert.Same(fixture.Editor, fixture.Surface.FocusedElement);
    }

    /// <summary>Checks IME caret uses the decorated body exactly once.</summary>
    [Fact]
    public void DecoratedEditorPublishesBodyCaretOffset()
    {
        using var plain = new Fixture();
        using var decorated = new Fixture(decorated: true);
        plain.Source.Scale = new(1.5f, 2);
        decorated.Source.Scale = new(1.5f, 2);
        plain.Surface.TryFocus(plain.Editor);
        decorated.Surface.TryFocus(decorated.Editor);
        plain.Pump();
        decorated.Pump();
        Assert.Equal(plain.Connection.Geometry!.Caret.X + 13, decorated.Connection.Geometry!.Caret.X);
        Assert.Equal(plain.Connection.Geometry.Caret.Y + 7, decorated.Connection.Geometry.Caret.Y);
    }

    /// <summary>Checks border color does not edit text or republish identical IME geometry.</summary>
    [Fact]
    public void BorderColorChangeKeepsDocumentAndImeGeometry()
    {
        using var fixture = new Fixture(decorated: true);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextInputGeometry geometry = fixture.Connection.Geometry!;
        ulong revision = fixture.Document.CaptureSnapshot().Revision;
        var next = new Compose.Definitions.TextBox { LayoutProvider = fixture.Provider, Text = string.Empty, TestId = "editor" };
        Lumyte.UI.Compose.Padding.Sides(13, 7, 2, 3)(next);
        Lumyte.UI.Compose.Border.Stroke(new(0, 1, 0, 1), 0)(next);
        fixture.Surface.Rebuild(next);
        fixture.Pump();
        Assert.Equal(revision, fixture.Document.CaptureSnapshot().Revision);
        Assert.Same(geometry, fixture.Connection.Geometry);
        Assert.Equal(0, fixture.Changes);
    }

    /// <summary>Checks unchanged geometry is not republished, while document and clipping changes invalidate it.</summary>
    [Fact]
    public void GeometryPublicationTracksOnlyGeometryChanges()
    {
        using var fixture = new Fixture();
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        TextInputGeometry original = fixture.Connection.Geometry!;
        for (int index = 0; index < 20; index++)
        {
            fixture.Connection.PublishGeometry();
        }

        Assert.Same(original, fixture.Connection.Geometry);
        fixture.Source.Size = new(80, 60);
        fixture.Pump();
        Assert.NotSame(original, fixture.Connection.Geometry);
        TextInputGeometry resized = fixture.Connection.Geometry!;
        Assert.True(fixture.Document.TryReplace(fixture.Document.CaptureSnapshot().Revision, new(0, 0), "abc", new(3, 3), TextEditOrigin.Keyboard));
        fixture.Pump();
        Assert.NotSame(resized, fixture.Connection.Geometry);
        Assert.Equal(fixture.Document.CaptureSnapshot().Revision, fixture.Connection.Geometry!.DocumentRevision);
    }

    /// <summary>Fill Shares Focus Lease And Changes Document Without Leaking Value.</summary>
    /// <returns>The operation result.</returns>
    [Fact]
    public async Task FillSharesFocusLeaseAndChangesDocumentWithoutLeakingValueAsync()
    {
        using var fixture = new Fixture();
        await using var host = new UiSampleAutomationHost(
 fixture.Surface,
 _ =>
        {
            fixture.Pump();
            return ValueTask.CompletedTask;
        });
        var editing = new UiTextAutomationHost(host, fixture.Connection, allowTextInspection: true);
        var page = new UiAutomationPage(editing);
        await page.GetByTestId("editor").FillAsync("private 😀");
        fixture.Document.TrySetSelection(fixture.Document.CaptureSnapshot().Revision, new(0, 0), TextEditOrigin.Keyboard);
        fixture.Pump();
        await page.Expect(page.GetByTestId("editor")).ToHaveValueAsync("private 😀");
        Assert.Equal("private 😀", fixture.Document.CaptureSnapshot().CommittedText);
        Assert.Equal(1, fixture.Changes);
        Assert.Equal(TextEditOrigin.Automation, fixture.LastOrigin);
        Assert.Equal(1, fixture.Service.Acquisitions);
        Assert.Equal(0, fixture.Service.Gates);
        Assert.DoesNotContain("private", string.Join(",", fixture.Surface.CaptureAutomationSnapshot().Nodes.Select(n => n.Name)));
    }

    /// <summary>Focus Loss Drains Old Session Before New Attachment And Publishes Clipped Geometry.</summary>
    [Fact]
    public void FocusLossDrainsOldSessionBeforeNewAttachmentAndPublishesClippedGeometry()
    {
        using var fixture = new Fixture();
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        Session first = fixture.Service.Current!;
        Assert.NotNull(first.Geometry);
        Assert.Equal(fixture.Document.CaptureSnapshot().Revision, first.Geometry!.DocumentRevision);
        Assert.True(first.Geometry.Viewport.Width <= fixture.Source.Size.X);
        fixture.Surface.TryFocus(null);
        fixture.Connection.SynchronizeFocus();
        Assert.False(first.Status.IsActive);
        fixture.Pump();
        Assert.True(first.IsReleased);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        Assert.NotEqual(first.Status.SessionId, fixture.Service.Current!.Status.SessionId);
    }

    /// <summary>Fill Does Not Cancel Composition And Inspection Defaults To Disabled.</summary>
    /// <returns>The operation result.</returns>
    [Fact]
    public async Task FillDoesNotCancelCompositionAndInspectionDefaultsToDisabledAsync()
    {
        using var fixture = new Fixture();
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        using (TextDocumentTransaction edit = fixture.Document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 0), []));
            edit.Replace(new(0, 0), "かな", new(2, 2));
            edit.Commit();
        }

        await using var host = new UiSampleAutomationHost(fixture.Surface, _ => ValueTask.CompletedTask);
        var editing = new UiTextAutomationHost(host, fixture.Connection);
        Assert.Throws<NotSupportedException>(() => editing.ReadValue(fixture.Surface, fixture.Editor.AutomationId));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await editing.AcquireTextInputAsync(canceled.Token));
        Assert.NotNull(fixture.Document.CaptureSnapshot().Composition);
        Assert.Equal(0, fixture.Service.Gates);
    }

    /// <summary>Checks an independently released backend is never configured again by candidate cleanup.</summary>
    [Fact]
    public void CandidateControllerDoesNotTouchReleasedSession()
    {
        using var fixture = new Fixture();
        using var popups = new UiPopupManager(fixture.Windows);
        using var controller = new UiImeCandidateController(fixture.Connection, new(fixture.Windows, fixture.Editor.Widget.LayoutProvider!), popups);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        controller.Advance();
        Session session = fixture.Service.Current!;
        fixture.Surface.TryFocus(null);
        fixture.Pump();
        Assert.True(session.IsReleased);
        controller.Advance();
        controller.BeginStop();
        for (int index = 0; index < 12; index++)
        {
            fixture.Pump();
            controller.Advance();
            popups.Advance();
        }

        Assert.True(controller.IsStopped);
        popups.BeginStop();
        popups.Advance();
        Assert.True(popups.IsStopped);
    }

    /// <summary>Checks hidden preparation, candidate commands and complete presentation retirement.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task CandidateControllerPreparesBeforeSuppressionAndRejectsStaleCommandsAsync()
    {
        using var fixture = new Fixture();
        var popups = new UiPopupManager(fixture.Windows);
        var presenter = new UiImeCandidatePresenter(fixture.Windows, fixture.Editor.Widget.LayoutProvider!);
        using var controller = new UiImeCandidateController(fixture.Connection, presenter, popups, new() { Policy = ImeCandidateUiPolicy.PreferCustom, });
        Task prepared = controller.PrepareAsync().AsTask();
        void Pump()
        {
            fixture.Pump();
            controller.Advance();
            controller.PublishGeometry();
            popups.Advance();
            fixture.Source.Present();
        }

        for (int frame = 0; frame < 20; frame++)
        {
            Pump();
        }

        Assert.True(prepared.IsCompletedSuccessfully);
        fixture.Surface.TryFocus(fixture.Editor);
        fixture.Pump();
        Session backend = fixture.Service.Current!;
        backend.Candidates = new(new(backend.Status.SessionId, backend.Generation, 1), 1, fixture.Document.CaptureSnapshot().Revision, [new(0, "候補")], 0, [0], 0, ImeCandidateOperations.Select | ImeCandidateOperations.Finalize);
        for (int frame = 0; frame < 20; frame++)
        {
            Pump();
        }

        ImeCandidateSnapshot snapshot = Assert.IsType<ImeCandidateSnapshot>(controller.CaptureSnapshot());
        Assert.Equal(ImeCandidatePresentationState.Custom, controller.State.State);
        Assert.Same(fixture.Editor, fixture.Surface.FocusedElement);
        var stale = new ImeCandidateAction(snapshot.Id, snapshot.Revision + 1, ImeCandidateActionKind.Select, 0);
        Task<ImeCandidateResult> rejected = controller.RequestAsync(stale).AsTask();
        Pump();
        Pump();
        Assert.False((await rejected).Accepted);
        var command = new ImeCandidateAction(snapshot.Id, snapshot.Revision, ImeCandidateActionKind.Select, 0);
        Task<ImeCandidateResult> accepted = controller.RequestAsync(command).AsTask();
        Pump();
        Pump();
        Assert.True((await accepted).Accepted);
        controller.BeginStop();
        for (int frame = 0; frame < 30; frame++)
        {
            Pump();
        }

        Assert.True(controller.IsStopped);
        controller.Dispose();
        popups.BeginStop();
        popups.Advance();
        Assert.True(popups.IsStopped);
        popups.Dispose();
        Assert.All(fixture.Source.Presentations.Skip(1), presentation => Assert.True(presentation.Disposed));
    }

    /// <summary>Checks sibling editors share the surface registration without handler construction arguments.</summary>
    [Fact]
    public void SurfaceConnectionAutomaticallyHandlesAllEditors()
    {
        using var fixture = new Fixture(multipleEditors: true);
        Assert.True(fixture.Surface.TryFocus(fixture.Editor));
        fixture.Pump();
        Session first = fixture.Service.Current!;
        Assert.Same(fixture.Document, first.Document);
        Assert.True(fixture.Surface.TryFocus(fixture.OtherEditor));
        fixture.Pump();
        Assert.True(first.IsReleased);
        Assert.Same(fixture.OtherEditor.Document, fixture.Service.Current!.Document);
        Assert.NotEqual(first.Status.SessionId, fixture.Service.Current.Status.SessionId);
    }

    /// <summary>Checks separate surfaces resolve separate handlers rather than a process-global current service.</summary>
    [Fact]
    public void AutomaticInputScopeIsIsolatedPerSurface()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        Assert.True(first.Surface.TryFocus(first.Editor));
        Assert.True(second.Surface.TryFocus(second.Editor));
        first.Pump();
        second.Pump();
        Assert.Same(first.Document, first.Service.Current!.Document);
        Assert.Same(second.Document, second.Service.Current!.Document);
        Assert.NotSame(first.Service.Current, second.Service.Current);
    }

    /// <summary>Checks the normal string/callback API survives rebuilt declarations and held-key repeats.</summary>
    [Fact]
    public void ControlledTextFeedbackPreservesSelectionAndHeldKeySeries()
    {
        using var fixture = new Fixture();
        string value = "abc";
        int changes = 0;
        Compose.Definitions.TextBox Build() => Compose.TextBox(text: value, layoutProvider: fixture.Provider, onTextChanged: change =>
        {
            value = change.Text;
            changes++;
            fixture.Surface.Rebuild(Build());
        });
        Compose.Definitions.TextBox initial = Build();
        fixture.Surface.Rebuild(initial);
        fixture.Pump();
        Assert.True(fixture.Surface.TryFocus(Element(fixture.Surface, initial)));
        fixture.Pump();
        TextDocument document = Element(fixture.Surface, initial).Document;
        Guid session = fixture.Service.Current!.Status.SessionId;
        Assert.True(document.TrySetSelection(document.CaptureSnapshot().Revision, new(3, 3), TextEditOrigin.Keyboard));
        Assert.True(fixture.Surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Backspace, true)));
        fixture.Pump();
        Compose.Definitions.TextBox rebuilt = Assert.IsType<Compose.Definitions.TextBox>(fixture.Surface.Root);
        Assert.NotSame(initial, rebuilt);
        Assert.Same(document, Element(fixture.Surface, rebuilt).Document);
        Assert.Equal("ab", value);
        Assert.Equal(2, document.CaptureSnapshot().Selection.Active);
        Assert.True(fixture.Surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Backspace, true, true)));
        fixture.Pump();
        Assert.Equal("a", value);
        Assert.Equal(2, changes);
        Assert.Equal(session, fixture.Service.Current!.Status.SessionId);
        Assert.True(fixture.Surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Backspace, false)));
    }

    /// <summary>Checks unrelated rebuilds keep the native session and active composition when the committed value agrees.</summary>
    [Fact]
    public void RebuildPreservesPreeditSelectionAndTheActiveSession()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Surface.TryFocus(fixture.Editor));
        fixture.Pump();
        Session session = fixture.Service.Current!;
        TextDocument document = fixture.Document;
        var composition = new TextCompositionSnapshot(Guid.NewGuid(), new(0, 0), []);
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(composition);
            edit.Replace(new(0, 0), "かな", new(1, 1));
            edit.Commit();
        }

        Compose.Definitions.TextBox next = Compose.TextBox(text: string.Empty, layoutProvider: fixture.Provider);
        fixture.Surface.Rebuild(next);
        fixture.Pump();
        Assert.Same(document, Element(fixture.Surface, next).Document);
        Assert.Same(session, fixture.Service.Current);
        Assert.True(session.Status.IsActive);
        Assert.Equal(composition.Id, document.CaptureSnapshot().Composition!.Id);
        Assert.Equal("かな", document.CaptureSnapshot().Text);
        Assert.Equal(1, document.CaptureSnapshot().Selection.Active);
        Assert.Same(Element(fixture.Surface, next), fixture.Surface.FocusedElement);
    }

    /// <summary>Checks source value changes wait for asynchronous IME retirement and never echo as an input callback.</summary>
    [Fact]
    public void ExternalValueWaitsForCompositionRetirementWithoutEcho()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Surface.TryFocus(fixture.Editor));
        fixture.Pump();
        Session previous = fixture.Service.Current!;
        previous.DelayRelease = true;
        TextDocument document = fixture.Document;
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 0), []));
            edit.Replace(new(0, 0), "かな", new(2, 2));
            edit.Commit();
        }

        int changes = 0;
        Compose.Definitions.TextBox next = Compose.TextBox(text: "server", layoutProvider: fixture.Provider, onTextChanged: _ => changes++);
        fixture.Surface.Rebuild(next);
        try
        {
            fixture.Pump();
            Assert.False(previous.IsReleased);
            Assert.NotEqual("server", document.CaptureSnapshot().Text);
        }
        finally
        {
            previous.DelayRelease = false;
        }

        fixture.Pump();
        Assert.True(previous.IsReleased);
        Assert.Equal("server", Element(fixture.Surface, next).Document.CaptureSnapshot().Text);
        Assert.Null(document.CaptureSnapshot().Composition);
        Assert.Equal(0, changes);
    }

    /// <summary>Checks a newly supplied value updates text without reporting it as another user edit.</summary>
    [Fact]
    public void ExternalValueUpdatesDoNotEchoAndKeepTheIdleSession()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Surface.TryFocus(fixture.Editor));
        fixture.Pump();
        Session previous = fixture.Service.Current!;
        int changes = 0;
        Compose.Definitions.TextBox next = Compose.TextBox(text: "external", layoutProvider: fixture.Provider, onTextChanged: _ => changes++);
        fixture.Surface.Rebuild(next);
        fixture.Pump();
        Assert.Equal("external", Element(fixture.Surface, next).Document.CaptureSnapshot().Text);
        Assert.Same(previous, fixture.Service.Current);
        Assert.Equal(0, changes);
    }

    /// <summary>Checks keyed sibling reorder transfers each document and the focused session independently.</summary>
    [Fact]
    public void KeyedEditorsRetainTheirOwnDocumentsWhenReordered()
    {
        using var fixture = new Fixture();
        Compose.Definitions.TextBox a = Compose.TextBox(key: "a", text: "first", layoutProvider: fixture.Provider);
        Compose.Definitions.TextBox b = Compose.TextBox(key: "b", text: "second", layoutProvider: fixture.Provider);
        fixture.Surface.Rebuild(Lumyte.UI.Compose.Stack()[a, b]);
        fixture.Pump();
        Assert.True(fixture.Surface.TryFocus(Element(fixture.Surface, a)));
        fixture.Pump();
        Session previous = fixture.Service.Current!;
        TextDocument documentA = Element(fixture.Surface, a).Document;
        TextDocument documentB = Element(fixture.Surface, b).Document;
        Compose.Definitions.TextBox nextB = Compose.TextBox(key: "b", text: "second", layoutProvider: fixture.Provider);
        Compose.Definitions.TextBox nextA = Compose.TextBox(key: "a", text: "first", layoutProvider: fixture.Provider);
        fixture.Surface.Rebuild(Lumyte.UI.Compose.Stack()[nextB, nextA]);
        fixture.Pump();
        Assert.Same(documentA, Element(fixture.Surface, nextA).Document);
        Assert.Same(documentB, Element(fixture.Surface, nextB).Document);
        Assert.Same(Element(fixture.Surface, nextA), fixture.Surface.FocusedElement);
        Assert.Same(previous, fixture.Service.Current);
    }

    /// <summary>Checks a parent can reject an edit by rebuilding with the unchanged controlled value.</summary>
    [Fact]
    public void ParentCanRejectAnEditWithoutAnEchoLoop()
    {
        using var fixture = new Fixture();
        int changes = 0;
        Compose.Definitions.TextBox Build() => Compose.TextBox(text: "accepted", layoutProvider: fixture.Provider, onTextChanged: _ =>
        {
            changes++;
            fixture.Surface.Rebuild(Build());
        });
        Compose.Definitions.TextBox editor = Build();
        fixture.Surface.Rebuild(editor);
        fixture.Pump();
        Assert.True(fixture.Surface.TryFocus(Element(fixture.Surface, editor)));
        fixture.Pump();
        TextDocument document = Element(fixture.Surface, editor).Document;
        Assert.True(document.TryReplace(document.CaptureSnapshot().Revision, new(0, 8), "rejected", new(8, 8), TextEditOrigin.Keyboard));
        fixture.Pump();
        Assert.Equal("accepted", document.CaptureSnapshot().CommittedText);
        Assert.Equal(1, changes);
    }

    /// <summary>Checks transformed parent values are applied once without being reported as another edit.</summary>
    [Fact]
    public void ParentCanTransformTheControlledValueWithoutAnEchoLoop()
    {
        using var fixture = new Fixture();
        string value = string.Empty;
        int changes = 0;
        Compose.Definitions.TextBox Build() => Compose.TextBox(text: value, layoutProvider: fixture.Provider, onTextChanged: change =>
        {
            changes++;
            value = change.Text.ToUpperInvariant();
            fixture.Surface.Rebuild(Build());
        });
        Compose.Definitions.TextBox editor = Build();
        fixture.Surface.Rebuild(editor);
        fixture.Pump();
        Assert.True(fixture.Surface.TryFocus(Element(fixture.Surface, editor)));
        fixture.Pump();
        TextDocument document = Element(fixture.Surface, editor).Document;
        Assert.True(document.TryReplace(document.CaptureSnapshot().Revision, new(0, 0), "mixed", new(5, 5), TextEditOrigin.Keyboard));
        fixture.Pump();
        Assert.Equal("MIXED", value);
        Assert.Equal(value, document.CaptureSnapshot().CommittedText);
        Assert.Equal(1, changes);
    }

    private static TextBoxElement Element(UiSurface surface, Lumyte.UI.Compose.Definitions.Widget declaration)
    {
        var pending = new Stack<UiElement>();
        pending.Push(surface.RootElement);
        while (pending.TryPop(out UiElement? element))
        {
            if (ReferenceEquals(element.Widget, declaration))
            {
                return (TextBoxElement)element;
            }

            foreach (UiElement child in element.Children)
            {
                pending.Push(child);
            }
        }

        throw new InvalidOperationException("The declaration is not mounted.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ParleyTextEngine2D _engine = new();
        private readonly UiWindowRegistration _registration;
        private readonly UiWindowBinding _binding;
        private bool _connectionDisposed;

        /// <summary>Initializes a new instance of the <see cref = "Fixture"/> class.</summary>
        /// <param name="multipleEditors">Whether to create sibling editors in one surface.</param>
        /// <param name="decorated">Whether to apply nested body offsets.</param>
        /// <param name="scrolled">Whether to wrap the editor in a real viewport.</param>
        /// <param name="numeric">An optional actual numeric editor declaration.</param>
        public Fixture(bool multipleEditors = false, bool decorated = false, bool scrolled = false, Compose.Definitions.NumericBox? numeric = null)
        {
            using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
            string family = _engine.AddFont(font);
            Provider = new ParleyUiTextLayoutProvider(_engine, new() { FontFamily = family });
            Compose.Definitions.TextBox editor = numeric ?? new Compose.Definitions.TextBox()
            {
                Text = string.Empty,
                LayoutProvider = Provider,
                TestId = "editor",
                OnTextChanged = change =>
                {
                    Changes++;
                    LastOrigin = change.Origin;
                },
            };
            editor.LayoutProvider = Provider;
            editor.TestId = "editor";
            var other = new Compose.Definitions.TextBox { Text = string.Empty, LayoutProvider = Provider, TestId = "other" };
            if (decorated)
            {
                Lumyte.UI.Compose.Padding.Sides(13, 7, 2, 3)(editor);
                Lumyte.UI.Compose.Border.Stroke(new(1, 0, 0, 1), 0)(editor);
            }

            Surface = new(scrolled ? Lumyte.UI.Compose.ScrollView(controller: Scroll)[Lumyte.UI.Compose.Stack()[editor, Lumyte.UI.Compose.SizedBox(height: 600)]] : multipleEditors ? Lumyte.UI.Compose.Stack()[editor, other] : editor);
            Editor = (TextBoxElement)(scrolled ? Surface.RootElement.Children[0].Children[0] : multipleEditors ? Surface.RootElement.Children[0] : Surface.RootElement);
            _registration = Windows.Register(Source);
            Task<UiWindowBinding> binding = Windows.BindPresentationAsync(_registration.Window, Surface).AsTask();
            for (int index = 0; index < 12; index++)
            {
                Windows.Advance();
                Source.Present();
            }

            _binding = binding.GetAwaiter().GetResult();
            Connection = new(Surface, Windows, _registration.Window, Service, new Resolver());
            Pump();
        }

        /// <summary>Gets the source.</summary>
        public TestSource Source { get; } = new();

        /// <summary>Gets the borrowed scroll controller.</summary>
        public ScrollController Scroll { get; } = new();

        /// <summary>Gets the windows.</summary>
        public HostedUiWindowManager Windows { get; } = new();

        /// <summary>Gets the service.</summary>
        public Backend Service { get; } = new();

        /// <summary>Gets the document.</summary>
        public TextDocument Document => Editor.Document;

        /// <summary>Gets the borrowed concrete layout provider for construction expressions.</summary>
        public ParleyUiTextLayoutProvider Provider { get; }

        /// <summary>Gets the editor.</summary>
        public TextBoxElement Editor { get; }

        /// <summary>Gets the surface.</summary>
        public UiSurface Surface { get; }

        /// <summary>Gets the connection.</summary>
        public UiTextInputConnection Connection { get; }

        /// <summary>Gets the second editor used to check surface-wide automatic routing.</summary>
        public TextBoxElement OtherEditor => (TextBoxElement)Surface.RootElement.Children[1];

        /// <summary>Gets the changes.</summary>
        public int Changes { get; private set; }

        /// <summary>Gets the origin of the committed edit notification.</summary>
        public TextEditOrigin? LastOrigin { get; private set; }

        /// <summary>Pump.</summary>
        public void Pump()
        {
            for (int index = 0; index < 4; index++)
            {
                Windows.Advance();
                Service.Advance();
                if (!_connectionDisposed)
                {
                    Connection.Advance();
                }

                Surface.SetViewport(Source.Size);
                Surface.UpdateLayout();
                if (!_connectionDisposed)
                {
                    Connection.PublishGeometry();
                }

                Source.Present();
            }
        }

        /// <summary>Dispose.</summary>
        public void Dispose()
        {
            Connection.BeginStop();
            Pump();
            Assert.True(Connection.IsStopped);
            Connection.Dispose();
            _connectionDisposed = true;
            Task retired = _binding.DisposeAsync().AsTask();
            Pump();
            Assert.True(retired.IsCompletedSuccessfully);
            _registration.Dispose();
            Pump();
            Windows.BeginStop();
            Pump();
            Windows.Dispose();
            Surface.Dispose();
            Scroll.Dispose();
            _engine.Dispose();
        }
    }

    private sealed class Resolver : IUiTextInputTargetResolver
    {
        /// <summary>Resolve.</summary>
        /// <param name = "focus">The focus.</param>
        /// <param name = "presentation">The presentation.</param>
        /// <param name = "surface">The surface.</param>
        /// <param name = "editor">The editor.</param>
        /// <returns>The operation result.</returns>
        public TextInputTarget Resolve(UiWindowId focus, UiWindowId presentation, Guid surface, Guid editor) => new(new WindowId(Guid.NewGuid(), 1), new WindowId(Guid.NewGuid(), 2), surface, editor);
    }

    private sealed class Backend : ITextInputService, ITextInputQuiescence, ITextInputInputGate
    {
        /// <summary>Gets the current.</summary>
        public Session? Current { get; private set; }

        /// <summary>Gets the gates.</summary>
        public int Gates { get; private set; }

        /// <summary>Gets the acquisitions.</summary>
        public int Acquisitions { get; private set; }

        /// <summary>Gets the features.</summary>
        public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

        /// <summary>Gets a value indicating whether this option is enabled.</summary>
        public bool IsInputIdle => Current?.Status.IsComposing != true;

        /// <summary>Gets a value indicating whether this option is enabled.</summary>
        public bool IsCandidateInputIdle => true;

        /// <summary>Begin.</summary>
        /// <param name = "target">The target.</param>
        /// <param name = "document">The document.</param>
        /// <param name = "navigator">The navigator.</param>
        /// <returns>The operation result.</returns>
        public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) => Current = new(target, document, navigator);

        /// <summary>Advance.</summary>
        public void Advance() => Current?.Advance();

        /// <summary>Drain Keys.</summary>
        /// <returns>The operation result.</returns>
        public IReadOnlyList<TextInputKey> DrainKeys() => [];

        /// <summary>Dispose.</summary>
        public void Dispose()
        {
        }

        /// <summary>Acquire Input Gate.</summary>
        /// <param name = "allowComposition">The allow composition.</param>
        /// <returns>The operation result.</returns>
        public IDisposable AcquireInputGate(bool allowComposition = false)
        {
            Gates++;
            Acquisitions++;
            return new Gate(() => Gates--);
        }

        private sealed class Gate(Action release) : IDisposable
        {
            /// <summary>Dispose.</summary>
            public void Dispose() => release();
        }
    }

    private sealed class Session(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) : TextInputSessionBase(
 target,
 document,
 navigator,
 1)
    {
        private bool _custom;

        /// <summary>Gets or sets the candidates.</summary>
        public ImeCandidateSnapshot? Candidates { get; set; }

        /// <summary>Gets the candidate support.</summary>
        public override ImeCandidateSupport CandidateSupport => new(true, true, true, string.Empty);

        /// <summary>Gets the candidate state.</summary>
        public override ImeCandidateState CandidateState => new(Candidates?.Id, _custom && Candidates is not null ? ImeCandidatePresentationState.Custom : ImeCandidatePresentationState.System, string.Empty);

        internal bool DelayRelease { get; set; }

        protected override bool CanRelease => !DelayRelease;

        /// <summary>Configure Candidate Ui.</summary>
        /// <param name = "policy">The policy.</param>
        /// <param name = "prepared">The prepared.</param>
        public override void ConfigureCandidateUi(ImeCandidateUiPolicy policy, bool prepared)
        {
            base.ConfigureCandidateUi(policy, prepared);
            _custom = policy == ImeCandidateUiPolicy.PreferCustom && prepared;
        }

        /// <summary>Capture Candidates.</summary>
        /// <returns>The operation result.</returns>
        public override ImeCandidateSnapshot? CaptureCandidates() => _custom ? Candidates : null;

        /// <summary>Request Candidate Async.</summary>
        /// <param name = "action">The action.</param>
        /// <param name = "cancellationToken">The cancellation token.</param>
        /// <returns>The operation result.</returns>
        public override ValueTask<ImeCandidateResult> RequestCandidateAsync(ImeCandidateAction action, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ImeCandidateResult(action, Candidates?.Id == action.Id && Candidates.Revision == action.ExpectedRevision, "fixture"));

        /// <summary>Return Candidates To System Async.</summary>
        /// <param name = "cancellationToken">The cancellation token.</param>
        /// <returns>The operation result.</returns>
        public override ValueTask ReturnCandidatesToSystemAsync(CancellationToken cancellationToken = default)
        {
            _custom = false;
            return ValueTask.CompletedTask;
        }

        /// <summary>Release Core.</summary>
        protected override void ReleaseCore()
        {
        }
    }
}
