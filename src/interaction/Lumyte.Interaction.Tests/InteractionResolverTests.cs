using Xunit;

namespace Lumyte.Interaction.Tests;

public sealed class InteractionResolverTests
{
    [Fact]
    public void HigherPriorityEligibleCandidateWins()
    {
        var context = new InteractionContext();
        InteractionCandidate<string>[] candidates =
        [
            new("default"),
            new("preferred") { Priority = 10 },
        ];

        InteractionResolution<string> result = new InteractionResolver().Resolve(candidates, context);

        InteractionResolution<string>.Match match = Assert.IsType<InteractionResolution<string>.Match>(result);
        Assert.Equal("preferred", match.Candidate.Value);
    }

    [Fact]
    public void IneligibleCandidatesDoNotParticipate()
    {
        var enabled = ContextKey.Create<bool>("enabled");
        var context = new InteractionContext();
        InteractionCandidate<string>[] candidates =
        [
            new("disabled") { When = enabled.Is(true), Priority = 10 },
            new("fallback"),
        ];

        InteractionResolution<string> result = new InteractionResolver().Resolve(candidates, context);

        InteractionResolution<string>.Match match = Assert.IsType<InteractionResolution<string>.Match>(result);
        Assert.Equal("fallback", match.Candidate.Value);
    }

    [Fact]
    public void EquallyRankedCandidatesReportAConflict()
    {
        InteractionCandidate<string>[] candidates = [new("first"), new("second")];

        InteractionResolution<string> result =
            new InteractionResolver().Resolve(candidates, new InteractionContext());

        InteractionResolution<string>.Conflict conflict = Assert.IsType<InteractionResolution<string>.Conflict>(result);
        Assert.Equal(["first", "second"], conflict.Candidates.Select(candidate => candidate.Value));
    }
}
