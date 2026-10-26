using System.Numerics;
using Lumyte.Input.Actions;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies explicit and compatibility names preserve the action update boundary.</summary>
public sealed class DeferredApiTests
{
    /// <summary>Checks queued settings remain invisible until the next action update.</summary>
    /// <param name="explicitQueue">Whether to use the explicit Queue API.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileAndBufferChangesCommitOnAdvance(bool explicitQueue)
    {
        var original = new ActionProfile([], [], [], []);
        ActionProfile replacement = original with { Actions = [new ActionDefinition("jump", ActionValueKind.Button)] };
        using var actions = new ActionSystem(original);
        ActionInputBuffer oldBuffer = actions.Buffer;
        var options = new InputBufferOptions(TimeSpan.FromSeconds(1), 2);
        if (explicitQueue)
        {
            actions.QueueProfile(replacement);
            actions.QueueBufferOptions(options);
        }
        else
        {
            actions.ApplyProfile(replacement);
            actions.SetBufferOptions(options);
        }

        Assert.Same(original, actions.ExportProfile());
        Assert.Same(oldBuffer, actions.Buffer);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        Assert.Same(replacement, actions.ExportProfile());
        Assert.NotSame(oldBuffer, actions.Buffer);
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
    }

    /// <summary>Checks a successful TryConsume exposes a nonnull result to nullable analysis.</summary>
    [Fact]
    public void SuccessfulConsumeNeedsNoNullSuppression()
    {
        var buffer = new ActionInputBuffer(new InputBufferOptions(TimeSpan.FromSeconds(1), 2));
        buffer.Add(new RecognizedAction("press", "game", TimeSpan.Zero, Vector2.One, []), TimeSpan.Zero);
        if (buffer.TryConsume("game", "press", TimeSpan.Zero, out RecognizedAction? action))
        {
            Assert.Equal("press", action.RecognitionId);
        }
        else
        {
            Assert.Fail("Expected the queued recognition.");
        }

        Assert.False(buffer.TryConsume("press", TimeSpan.Zero, out RecognizedAction? missing));
        Assert.Null(missing);
    }
}
