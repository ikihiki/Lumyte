using Lumyte.Graphics.BenchmarkWorkloads;
using Xunit;

namespace Lumyte.Graphics.Benchmarks.Tests;

/// <summary>Deterministic bounded mutation counts and logical roundtrip tests.</summary>
public sealed class SceneMutationFixtureTests
{
    /// <summary>Checks all parent/individual and shared/distinct placement combinations.</summary>
    [Fact]
    public void PlacementHasEquivalentLogicalInputs()
    {
        foreach (string placement in new[] { "parent", "individual" })
        {
            foreach (bool shared in new[] { false, true })
            {
                using var fixture = new SceneMutationFixture("D03", placement, shared);
                for (int frame = 1; frame <= 8; frame++)
                {
                    fixture.Apply(frame);
                    Assert.Equal(placement == "parent" ? 1 : 256, fixture.PropertyEdits);
                    Assert.Equal(0, fixture.StructuralEdits);
                    Assert.Equal(256, fixture.ElementCount);
                    Assert.Equal(256, SceneMutationFixture.Reference("D03", frame).Count);
                    Assert.Equal(SceneMutationFixture.InputId("D03", frame), SceneMutationFixture.InputId("D03", frame + 2));
                }
            }
        }
    }

    /// <summary>Checks insertion/removal/reorder/reparent return to a bounded initial state repeatedly.</summary>
    [Fact]
    public void StructuralTraceIsBoundedRoundtrip()
    {
        using var fixture = new SceneMutationFixture("D11");
        for (int frame = 1; frame <= 40; frame++)
        {
            fixture.Apply(frame);
            Assert.Equal(frame % 4 == 1 ? 4 : 3, fixture.ElementCount);
            Assert.Equal(frame % 2 == 1 ? 1 : 2, fixture.StructuralEdits);
            Assert.Equal(0, fixture.PropertyEdits);
            Assert.Equal(fixture.ElementCount, SceneMutationFixture.Reference("D11", frame).Count);
            Assert.Equal(SceneMutationFixture.InputId("D11", frame), SceneMutationFixture.InputId("D11", frame + 4));
        }

        Assert.Throws<ArgumentException>(() => fixture.Apply(42));
    }
}
