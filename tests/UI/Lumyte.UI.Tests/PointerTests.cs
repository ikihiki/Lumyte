using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks logical pointer routing, component-defined input traversal, capture and callback lifetime.</summary>
public sealed class PointerTests
{
    /// <summary>Checks an unchanged input view is prepared once and cached Move dispatch performs no managed allocation.</summary>
    [Fact]
    public void CapturedMovesReuseInputSelectionAndAllocateNoSnapshots()
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton();
        root.Embeddeds.Initialize(button);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down));
        int queries = root.InputQueries;
        var move = new UiPointerInput(0, UiPointerPhase.Move, new(2, 2));
        for (int index = 0; index < 256; index++)
        {
            surface.Dispatch(move);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            surface.Dispatch(move);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(queries, root.InputQueries);
        surface.UpdateLayout();
        Assert.Equal(queries, root.InputQueries);
        root.Select();
        surface.UpdateLayout();
        Assert.Equal(queries + 1, root.InputQueries);
        Assert.False(button.IsPressed);
    }

    /// <summary>Checks invalidating input does not prepare it during Dispatch, including before any capture exists.</summary>
    [Fact]
    public void DispatchOnlyReadsInputPreparedByUpdateLayout()
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton();
        root.Embeddeds.Initialize(button);
        using UiSurface surface = Surface(root);
        int queries = root.InputQueries;
        root.Select();
        Assert.True(Send(surface, UiPointerPhase.Down));
        Assert.Equal(queries, root.InputQueries);
        surface.UpdateLayout();
        Assert.Equal(queries + 1, root.InputQueries);
        Assert.False(button.IsPressed);
        Assert.False(Send(surface, UiPointerPhase.Down));
    }

    /// <summary>Checks reconnecting a tree cannot reuse input eligibility from a previous surface.</summary>
    [Fact]
    public void ReconnectedExcludedRootDoesNotReusePreviousInputGeometry()
    {
        Definitions.Panel root = Panel()[MakeButton()];
        using (UiSurface previous = Surface(root))
        {
            Click(previous);
        }

        root.IsEnabled = false;
        using UiSurface next = Surface(root);
        Assert.False(Send(next, UiPointerPhase.Down));
    }

    /// <summary>Checks input preparation aborts on failure and cancellation is independent of geometry.</summary>
    [Fact]
    public void InputPreparationFailureAbortsUpdateAndCancellationStillWorks()
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton();
        root.Embeddeds.Initialize(button);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down));
        root.Select(Panel());
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.True(Send(surface, UiPointerPhase.Cancel));
        Assert.False(button.IsPressed);
        root.Select(button);
        surface.UpdateLayout();
        Click(surface);
    }

    /// <summary>Checks later one-time initialization invalidates a prepared view through layout, including geometry and input membership.</summary>
    [Fact]
    public void LateContentInitializationRebuildsTheInputView()
    {
        Definitions.Panel root = Panel();
        using UiSurface surface = Surface(root);
        Assert.False(Send(surface, UiPointerPhase.Down));
        int clicks = 0;
        root.Children.Initialize(MakeButton(() => clicks++));
        surface.UpdateLayout();
        Click(surface);
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks the trusted owned-collection path still rejects a nonempty collection belonging to another widget.</summary>
    [Fact]
    public void InputSelectionCannotUseAnotherWidgetsOwnedChildren()
    {
        var root = new InputPanel();
        Definitions.Panel foreign = Panel()[MakeButton()];
        root.SetQuery(() => foreign.Children);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
    }

    /// <summary>Checks viewport changes refresh cached clips without cancelling structural capture.</summary>
    [Fact]
    public void EmptyViewportRetainsCaptureAndRestoresPressedStateOnResize()
    {
        Definitions.Button button = MakeButton();
        using UiSurface surface = Surface(Panel()[button]);
        Send(surface, UiPointerPhase.Down);
        surface.SetViewport(Vector2.Zero);
        surface.UpdateLayout();
        Assert.False(button.IsPressed);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.True(button.IsPressed);
        Assert.True(Send(surface, UiPointerPhase.Up));
    }

    /// <summary>Checks generated callback arguments accept lambdas and method groups without calling them during construction.</summary>
    [Fact]
    public void CallbacksAreOrdinaryFactoryArguments()
    {
        int calls = 0;
        void Clicked() => calls++;
        Definitions.Button button = Button(onClick: Clicked)[Panel(width: 20, height: 10), Button.Background()[Panel()]];
        Definitions.Button lambda = Button(onClick: () => calls++);
        Assert.Equal(0, calls);
        Assert.NotNull(lambda.OnClick);
        Assert.Null(Button().OnClick);
        Assert.Null(Button(onClick: null).OnClick);
        using UiSurface surface = Surface(Panel()[button]);
        Assert.True(Send(surface, UiPointerPhase.Down));
        Assert.True(button.IsPressed);
        Assert.Equal(0, calls);
        Assert.True(Send(surface, UiPointerPhase.Up));
        Assert.False(button.IsPressed);
        Assert.Equal(1, calls);
    }

    /// <summary>Checks the half-open bounds and viewport policy.</summary>
    /// <param name="x">The pointer X coordinate.</param>
    /// <param name="y">The pointer Y coordinate.</param>
    /// <param name="hit">Whether the position is inside.</param>
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(19.9f, 9.9f, true)]
    [InlineData(20, 5, false)]
    [InlineData(5, 10, false)]
    [InlineData(-1, 5, false)]
    [InlineData(5, -1, false)]
    [InlineData(100, 0, false)]
    public void HitBoundsIncludeOnlyLeftAndTop(float x, float y, bool hit)
    {
        int calls = 0;
        Definitions.Button button = MakeButton(() => calls++);
        using UiSurface surface = Surface(Panel()[button]);
        Assert.Equal(hit, Send(surface, UiPointerPhase.Down, new(x, y)));
        Assert.Equal(hit, Send(surface, UiPointerPhase.Up, new(x, y)));
        Assert.Equal(hit ? 1 : 0, calls);
    }

    /// <summary>Checks front-to-back hit order, including transparent non-button blockers.</summary>
    [Fact]
    public void FrontmostContentBlocksButtonsBehindIt()
    {
        int behind = 0;
        int front = 0;
        using (UiSurface surface = Surface(Panel()[MakeButton(() => behind++), MakeButton(() => front++)]))
        {
            Click(surface);
        }

        Assert.Equal(0, behind);
        Assert.Equal(1, front);
        using UiSurface blocked = Surface(Panel()[MakeButton(() => behind++), Rectangle(width: 20, height: 10)]);
        Assert.False(Send(blocked, UiPointerPhase.Down));
        Assert.Equal(0, behind);
    }

    /// <summary>Checks child content routes to its nearest button and nested buttons do not activate their ancestors.</summary>
    [Fact]
    public void NestedButtonsActivateOnlyTheNearestButton()
    {
        int outerCalls = 0;
        int innerCalls = 0;
        Definitions.Button inner = MakeButton(() => innerCalls++);
        Definitions.Button outer = Button(onClick: () => outerCalls++)[inner];
        using UiSurface surface = Surface(Panel()[outer]);
        Click(surface);
        Assert.Equal(0, outerCalls);
        Assert.Equal(1, innerCalls);
        Assert.False(outer.IsPressed);
    }

    /// <summary>Checks Button finds its owner through custom widgets with input groups, without a Button contract on Widget.</summary>
    [Fact]
    public void ButtonResolvesHitsThroughCustomInputContainers()
    {
        int outerCalls = 0;
        int innerCalls = 0;
        var content = new InputPanel();
        Definitions.Panel plain = Panel(width: 20, height: 10);
        Definitions.Button inner = MakeButton(() => innerCalls++);
        content.Embeddeds.Initialize(plain);
        content.Foreground.Initialize(inner);
        content.Select(plain);
        Definitions.Button outer = Button(width: 20, height: 10, onClick: () => outerCalls++)[content];
        using UiSurface surface = Surface(Panel()[outer]);
        Click(surface);
        Assert.Equal(1, outerCalls);
        Assert.Equal(0, innerCalls);
        content.Select(inner);
        surface.UpdateLayout();
        Click(surface);
        Assert.Equal(1, outerCalls);
        Assert.Equal(1, innerCalls);
    }

    /// <summary>Checks Button owns its background but does not expose background buttons to input.</summary>
    [Fact]
    public void ButtonDefinesWhichGroupsAcceptInput()
    {
        int outerCalls = 0;
        int backgroundCalls = 0;
        Definitions.Button background = MakeButton(() => backgroundCalls++);
        Definitions.Button outer = Button(onClick: () => outerCalls++)[Panel(width: 20, height: 10), Button.Background()[background]];
        using UiSurface surface = Surface(Panel()[outer]);
        Click(surface);
        Assert.Equal(1, outerCalls);
        Assert.Equal(0, backgroundCalls);
    }

    /// <summary>Checks custom components choose input groups independently of registration order.</summary>
    [Fact]
    public void CustomInputGroupsAndOrderAreRespected()
    {
        int embeddedCalls = 0;
        int foregroundCalls = 0;
        var root = new InputPanel();
        Definitions.Button embedded = MakeButton(() => embeddedCalls++);
        Definitions.Button foreground = MakeButton(() => foregroundCalls++);
        root.Embeddeds.Initialize(embedded);
        root.Foreground.Initialize(foreground);
        root.Select(foreground, embedded);
        using UiSurface surface = Surface(root);
        Click(surface);
        Assert.Equal(1, embeddedCalls);
        Assert.Equal(0, foregroundCalls);
    }

    /// <summary>Checks malformed component traversal fails before creating a press or invoking a callback.</summary>
    /// <param name="failure">The invalid selection.</param>
    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("null-sequence")]
    public void InvalidInputChildrenCannotCreateCapture(string failure)
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton(() => throw new InvalidOperationException("click"));
        root.Embeddeds.Initialize(button);
        root.Select(failure switch
        {
            "foreign" => [Panel()],
            "duplicate" => [button, button],
            _ => [null!],
        });

        if (failure == "null-sequence")
        {
            root.SetQuery(() => null!);
        }

        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.False(button.IsPressed);
        Assert.False(surface.Dispatch(new UiFocusLostInput()));
    }

    /// <summary>Checks a custom component can withdraw a captured child from its input selection.</summary>
    [Fact]
    public void InputSelectionChangesReleaseExistingCapture()
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton(() => throw new InvalidOperationException("withdrawn"));
        root.Embeddeds.Initialize(button);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down));
        root.Select();
        surface.UpdateLayout();
        Assert.False(button.IsPressed);
        Assert.False(Send(surface, UiPointerPhase.Up));
    }

    /// <summary>Checks one failing loss notification does not retain another withdrawn pointer capture.</summary>
    [Fact]
    public void FailedCaptureLossStillCleansOtherWithdrawnCaptures()
    {
        var root = new InputPanel();
        var first = new ThrowingCaptureButton();
        first.Children.Initialize(Panel(width: 20, height: 10));
        Definitions.Button second = MakeButton();
        root.Embeddeds.Initialize(Stack()[first, second]);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down, new(2, 2), 1));
        Assert.True(Send(surface, UiPointerPhase.Down, new(2, 12), 2));
        Assert.True(first.IsPressed);
        Assert.True(second.IsPressed);
        root.Select();
        Assert.Throws<ArithmeticException>(surface.UpdateLayout);
        surface.UpdateLayout();
        Assert.False(first.IsPressed);
        Assert.False(second.IsPressed);
        Assert.False(Send(surface, UiPointerPhase.Move, new(2, 12), 2));
    }

    /// <summary>Checks input membership uses reference identity even if distinct widgets override value equality.</summary>
    [Fact]
    public void InputSelectionUsesReferenceIdentity()
    {
        var root = new InputPanel();
        var first = new EqualButton();
        var second = new EqualButton();
        first.Children.Initialize(Panel(width: 20, height: 10));
        second.Children.Initialize(Panel(width: 20, height: 10));
        root.Embeddeds.Initialize(first, second);
        root.Select(first);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down));
        Assert.True(first.IsPressed);
        root.Select(second);
        surface.UpdateLayout();
        Assert.False(first.IsPressed);
        Assert.False(Send(surface, UiPointerPhase.Up));
    }

    /// <summary>Checks cancellation never enumerates custom input children, even if their enumerator is now failing.</summary>
    [Fact]
    public void CancellationDoesNotRunCustomInputTraversal()
    {
        var root = new InputPanel();
        Definitions.Button button = MakeButton();
        root.Embeddeds.Initialize(button);
        using UiSurface surface = Surface(root);
        Send(surface, UiPointerPhase.Down);
        root.SetQuery(() => throw new ArithmeticException("input fixture"));
        Assert.True(Send(surface, UiPointerPhase.Cancel));
        Assert.False(button.IsPressed);
        Assert.True(surface.Dispatch(new UiFocusLostInput()));
    }

    /// <summary>Checks zero-area buttons cannot start a press.</summary>
    /// <param name="width">The button content width.</param>
    /// <param name="height">The button content height.</param>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(20, 0)]
    public void ZeroAreaButtonsAreNotHit(float width, float height)
    {
        Definitions.Button button = Button()[Panel(width: width, height: height)];
        using UiSurface surface = Surface(Panel()[button]);
        Assert.False(Send(surface, UiPointerPhase.Down, Vector2.Zero));
        Assert.False(button.IsPressed);
    }

    /// <summary>Checks un-clipped overflow can be hit while ancestor and viewport clips still apply.</summary>
    /// <param name="clip">Whether to clip at the small parent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowUsesTheSameParentClipAsDrawing(bool clip)
    {
        int calls = 0;
        var parent = new OffsetPanel(new(10, 0)) { Width = 5, Height = 5, ClipToBounds = clip };
        parent.Children.Initialize(MakeButton(() => calls++));
        using UiSurface surface = Surface(Panel()[parent]);
        Assert.Equal(!clip, Send(surface, UiPointerPhase.Down, new(12, 2)));
        Assert.Equal(!clip, Send(surface, UiPointerPhase.Up, new(12, 2)));
        Assert.Equal(clip ? 0 : 1, calls);
    }

    /// <summary>Checks overflow from a button's content does not enlarge the button's own clickable area.</summary>
    [Fact]
    public void DownMustAlsoBeInsideTheButtonItself()
    {
        var overflow = new OffsetPanel(new(30, 0));
        overflow.Children.Initialize(Panel(width: 10, height: 10));
        Definitions.Button button = Button()[overflow];
        using UiSurface surface = Surface(Panel()[button]);
        Assert.False(Send(surface, UiPointerPhase.Down, new(32, 2)));
        Assert.False(button.IsPressed);
    }

    /// <summary>Checks invisible, disabled and non-hit-testable front subtrees do not block an enabled button.</summary>
    /// <param name="setting">The exclusion flag.</param>
    [Theory]
    [InlineData("visible")]
    [InlineData("enabled")]
    [InlineData("hit")]
    public void ExcludedSubtreesDoNotReceiveInput(string setting)
    {
        int calls = 0;
        Definitions.Panel excluded = Panel()[MakeButton(() => throw new InvalidOperationException("excluded"))];
        excluded.IsVisible = setting != "visible";
        excluded.IsEnabled = setting != "enabled";
        excluded.HitTestVisible = setting != "hit";
        using UiSurface surface = Surface(Panel()[MakeButton(() => calls++), excluded]);
        Click(surface);
        Assert.Equal(1, calls);
    }

    /// <summary>Checks capture survives leaving the button and clicking depends on the final position.</summary>
    /// <param name="returnInside">Whether the pointer returns before release.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureTracksOutsideAndInsidePositions(bool returnInside)
    {
        int calls = 0;
        Definitions.Button button = MakeButton(() => calls++);
        using UiSurface surface = Surface(Panel()[button]);
        Assert.True(Send(surface, UiPointerPhase.Down));
        Assert.True(Send(surface, UiPointerPhase.Move, new(50, 50)));
        Assert.False(button.IsPressed);
        Vector2 end = returnInside ? new(1, 1) : new(50, 50);
        if (returnInside)
        {
            Assert.True(Send(surface, UiPointerPhase.Move, end));
            Assert.True(button.IsPressed);
        }

        Assert.True(Send(surface, UiPointerPhase.Up, end));
        Assert.False(button.IsPressed);
        Assert.Equal(returnInside ? 1 : 0, calls);
        Assert.False(Send(surface, UiPointerPhase.Up, end));
        Assert.False(Send(surface, UiPointerPhase.Move, end));
    }

    /// <summary>Checks one owner per button, duplicate Down consumption and independent captures on different buttons.</summary>
    [Fact]
    public void PointerOwnersAreIndependentAndCannotRestartAPress()
    {
        int calls = 0;
        Definitions.Button first = MakeButton(() => calls++);
        Definitions.Button second = MakeButton(() => calls++);
        using UiSurface surface = Surface(Stack()[first, second]);
        Assert.True(Send(surface, UiPointerPhase.Down, id: 1));
        Assert.True(Send(surface, UiPointerPhase.Down, new(1, 11), 1));
        Assert.True(Send(surface, UiPointerPhase.Down, id: 2));
        Assert.False(Send(surface, UiPointerPhase.Up, id: 2));
        Assert.True(Send(surface, UiPointerPhase.Down, new(1, 11), 3));
        Assert.True(first.IsPressed);
        Assert.True(second.IsPressed);
        Assert.True(Send(surface, UiPointerPhase.Cancel, id: 1));
        Assert.False(first.IsPressed);
        Assert.True(second.IsPressed);
        Assert.True(Send(surface, UiPointerPhase.Up, new(1, 11), 3));
        Assert.Equal(1, calls);
    }

    /// <summary>Checks cancellation is idempotent, does not run layout and preserves unrelated captures.</summary>
    [Fact]
    public void CancelAndFocusLostWorkEvenWhenLayoutIsFailing()
    {
        var root = new FaultPanel();
        Definitions.Button first = MakeButton();
        Definitions.Button second = MakeButton();
        root.Children.Initialize(Stack()[first, second]);
        using UiSurface surface = Surface(root);
        Assert.True(Send(surface, UiPointerPhase.Down, id: 1));
        Assert.True(Send(surface, UiPointerPhase.Down, new(1, 11), 2));
        root.FailNextLayout();
        int calls = root.LayoutCalls;
        Assert.True(Send(surface, UiPointerPhase.Cancel, id: 1));
        Assert.False(Send(surface, UiPointerPhase.Cancel, id: 1));
        Assert.True(second.IsPressed);
        Assert.True(surface.Dispatch(new UiFocusLostInput()));
        Assert.False(surface.Dispatch(new UiFocusLostInput()));
        Assert.False(first.IsPressed);
        Assert.False(second.IsPressed);
        Assert.Equal(calls, root.LayoutCalls);
    }

    /// <summary>Checks normal dispatch does not trigger pending layout, and a failed explicit update can be cancelled.</summary>
    [Fact]
    public void DispatchDoesNotExecuteLayoutAndFailedUpdateCanBeCancelled()
    {
        var root = new FaultPanel();
        Definitions.Button first = MakeButton();
        Definitions.Button second = MakeButton();
        root.Children.Initialize(Stack()[first, second]);
        using UiSurface surface = Surface(root);
        Send(surface, UiPointerPhase.Down, id: 1);
        Send(surface, UiPointerPhase.Down, new(1, 11), 2);
        root.FailNextLayout();
        int calls = root.LayoutCalls;
        Assert.True(Send(surface, UiPointerPhase.Move, id: 1));
        Assert.Equal(calls, root.LayoutCalls);
        Assert.Throws<ArithmeticException>(surface.UpdateLayout);
        Assert.True(Send(surface, UiPointerPhase.Cancel, id: 1));
        Assert.False(first.IsPressed);
        Assert.True(second.IsPressed);
        Assert.True(Send(surface, UiPointerPhase.Cancel, id: 2));
    }

    /// <summary>Checks successful layout refreshes a captured pointer without requiring another Move event.</summary>
    [Fact]
    public void RepositioningRefreshesThePressedVisualWithoutDroppingCapture()
    {
        int calls = 0;
        var root = new OffsetPanel(Vector2.Zero);
        Definitions.Button button = MakeButton(() => calls++);
        root.Children.Initialize(button);
        using UiSurface surface = Surface(root);
        Send(surface, UiPointerPhase.Down);
        root.MoveTo(new(30, 0));
        surface.UpdateLayout();
        Assert.False(button.IsPressed);
        root.MoveTo(Vector2.Zero);
        surface.UpdateLayout();
        Assert.True(button.IsPressed);
        Assert.True(Send(surface, UiPointerPhase.Up));
        Assert.Equal(1, calls);
    }

    /// <summary>Checks rebuilt declarations change eligibility while disposing the previous view cancels its capture.</summary>
    /// <param name="setting">The exclusion flag.</param>
    [Theory]
    [InlineData("visible")]
    [InlineData("enabled")]
    [InlineData("hit")]
    public void RebuiltEligibilityDoesNotRetainThePreviousViewsCapture(string setting)
    {
        Definitions.Button previous = MakeButton();
        using UiSurface previousSurface = Surface(Panel()[previous]);
        Send(previousSurface, UiPointerPhase.Down);
        previousSurface.Dispose();
        Assert.False(previous.IsPressed);
        Definitions.Button next = MakeButton();
        using UiSurface nextSurface = Surface(Panel(isVisible: setting != "visible", isEnabled: setting != "enabled", hitTestVisible: setting != "hit")[next]);
        Assert.False(Send(nextSurface, UiPointerPhase.Down));
        Assert.False(next.IsPressed);
    }

    /// <summary>Checks callbacks are fetched after release, including replacement and null during an active press.</summary>
    [Fact]
    public void ReleaseUsesTheLatestCallbackAndNullStillConsumesInput()
    {
        int calls = 0;
        Definitions.Button button = MakeButton(() => calls += 100);
        using UiSurface surface = Surface(Panel()[button]);
        Send(surface, UiPointerPhase.Down);
        button.OnClick = () => calls++;
        Send(surface, UiPointerPhase.Up);
        Assert.Equal(1, calls);
        Send(surface, UiPointerPhase.Down);
        button.OnClick = null;
        Assert.True(Send(surface, UiPointerPhase.Up));
        Assert.False(button.IsPressed);
        Assert.Equal(1, calls);
    }

    /// <summary>Checks self replacement and explicit nested dispatch see already released capture, without a phase guard.</summary>
    [Fact]
    public void CallbackCanReplaceItselfAndDispatchAnotherClick()
    {
        int first = 0;
        int next = 0;
        Definitions.Button button = MakeButton();
        using UiSurface surface = Surface(Panel()[button]);
        button.OnClick = () =>
        {
            Assert.False(button.IsPressed);
            first++;
            button.OnClick = () => next++;
            Click(surface);
        };
        Click(surface);
        Assert.Equal(1, first);
        Assert.Equal(1, next);
    }

    /// <summary>Checks delegate exception semantics while never leaving capture behind.</summary>
    [Fact]
    public void CallbackExceptionsPropagateAfterAllPressStateIsReleased()
    {
        int calls = 0;
        Action callback = () => calls++;
        callback += () => throw new ArithmeticException();
        callback += () => calls += 100;
        Definitions.Button button = MakeButton(callback);
        using UiSurface surface = Surface(Panel()[button]);
        Send(surface, UiPointerPhase.Down);
        Assert.Throws<ArithmeticException>(() => Send(surface, UiPointerPhase.Up));
        Assert.Equal(1, calls);
        Assert.False(button.IsPressed);
        Assert.True(surface.Dispatch(new UiFocusLostInput()));
        button.OnClick = () => calls++;
        Click(surface);
        Assert.Equal(2, calls);
    }

    /// <summary>Checks disposal from a callback clears other captures and does not access the disposed surface afterward.</summary>
    [Fact]
    public void CallbackMayDisposeTheSurfaceAfterRelease()
    {
        Definitions.Button first = MakeButton();
        Definitions.Button second = MakeButton();
        using UiSurface surface = Surface(Stack()[first, second]);
        first.OnClick = surface.Dispose;
        Send(surface, UiPointerPhase.Down, id: 1);
        Send(surface, UiPointerPhase.Down, new(1, 11), 2);
        Assert.True(Send(surface, UiPointerPhase.Up, id: 1));
        Assert.False(first.IsPressed);
        Assert.False(second.IsPressed);
        Assert.Throws<ObjectDisposedException>(() => surface.Dispatch(new UiFocusLostInput()));
    }

    /// <summary>Checks dispatch preserves sequential cross-thread use and surface-local pointer identities.</summary>
    /// <returns>The asynchronous sequential input verification.</returns>
    [Fact]
    public async Task DispatchHasNoCreatorThreadAffinityAndCapturesAreSurfaceLocalAsync()
    {
        int calls = 0;
        Definitions.Button first = MakeButton(() => calls++);
        Definitions.Button second = MakeButton(() => calls++);
        using UiSurface firstSurface = Surface(Panel()[first]);
        using UiSurface secondSurface = Surface(Panel()[second]);
        Assert.True(await Task.Run(() => Send(firstSurface, UiPointerPhase.Down)));
        Assert.True(Send(secondSurface, UiPointerPhase.Down));
        Assert.True(await Task.Run(() => Send(firstSurface, UiPointerPhase.Up)));
        Assert.True(second.IsPressed);
        Assert.True(Send(secondSurface, UiPointerPhase.Up));
        Assert.Equal(2, calls);
    }

    /// <summary>Checks input lifecycle and value validation without enum-definition checks or implicit layout for unknown input.</summary>
    [Fact]
    public void InvalidAndUnknownInputDoesNotCreatePressState()
    {
        var root = new FaultPanel();
        using UiSurface surface = Surface(root);
        Assert.Throws<ArgumentNullException>(() => surface.Dispatch(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Send(surface, UiPointerPhase.Down, id: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Send(surface, UiPointerPhase.Cancel, new(float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Send(surface, UiPointerPhase.Move, new(0, float.PositiveInfinity)));
        Assert.False(surface.Dispatch(new UnknownInput()));
        Assert.False(Send(surface, (UiPointerPhase)999));
        Assert.Equal(1, root.LayoutCalls);
        Assert.False(Send(surface, UiPointerPhase.Cancel));
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.Dispatch(new UiFocusLostInput()));
    }

    private static Definitions.Button MakeButton(Action? callback = null) => Button(onClick: callback)[Panel(width: 20, height: 10)];

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private static bool Send(UiSurface surface, UiPointerPhase phase, Vector2? position = null, long id = 0) =>
        surface.Dispatch(new UiPointerInput(id, phase, position ?? new(1, 1)));

    private static void Click(UiSurface surface)
    {
        Assert.True(Send(surface, UiPointerPhase.Down));
        Assert.True(Send(surface, UiPointerPhase.Up));
    }

    private sealed record UnknownInput : UiInput;

    private sealed class EqualButton : Definitions.Button
    {
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is EqualButton;

        /// <inheritdoc/>
        public override int GetHashCode() => 0;
    }

    private sealed class OffsetPanel(Vector2 offset) : Definitions.Panel
    {
        private Vector2 _offset = offset;

        public void MoveTo(Vector2 position)
        {
            _offset = position;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = Vector2.Zero;
            foreach (Widget child in Children)
            {
                size = Vector2.Max(size, LayoutChild(child, constraints.Loosen()));
                SetChildOffset(child, _offset);
            }

            return constraints.Constrain(size);
        }
    }

    private sealed class FaultPanel : Definitions.Panel
    {
        private bool _fail;

        public int LayoutCalls { get; private set; }

        public void FailNextLayout()
        {
            _fail = true;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            LayoutCalls++;
            if (_fail)
            {
                throw new ArithmeticException("layout fixture");
            }

            return base.LayoutOverride(constraints);
        }
    }

    private sealed class ThrowingCaptureButton : Definitions.Button
    {
        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            base.OnPointerCaptureChanged(state);
            if (!state.IsCaptured)
            {
                throw new ArithmeticException("capture loss fixture");
            }
        }
    }

    private sealed class InputPanel : Widget
    {
        private Widget[]? _selection;
        private Func<IEnumerable<Widget>>? _query;

        public InputPanel()
        {
            Embeddeds = CreateChildren();
            Foreground = CreateChildren();
        }

        public WidgetChildren Embeddeds { get; }

        public WidgetChildren Foreground { get; }

        public int InputQueries { get; private set; }

        public void Select(params Widget[] children)
        {
            _selection = children;
            InvalidateInput();
        }

        public void SetQuery(Func<IEnumerable<Widget>> query)
        {
            _query = query;
            InvalidateInput();
        }

        protected override IEnumerable<Widget> GetInputChildren()
        {
            InputQueries++;
            return _query is null ? _selection ?? base.GetInputChildren() : _query();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (Widget child in Embeddeds.Concat(Foreground))
            {
                LayoutChild(child, constraints.Loosen());
                SetChildOffset(child, Vector2.Zero);
            }

            return constraints.Constrain(Vector2.Zero);
        }
    }
}
