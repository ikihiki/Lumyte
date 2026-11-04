using System.Text.Json;
using Lumyte.Graphics.Vulkan;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmarks;

internal static class GraphChecks
{
    internal static void Run()
    {
        using var device = VulkanDevice.Create();
        foreach (GraphProfile profile in GraphProfile.All)
        {
            var fixture = new GraphFixture(device, profile);
            using Graph first = fixture.Build();
            using Graph second = fixture.Build();
            string baseline = Verify(first, profile);
            Require(baseline == Verify(first, profile), "Repeat compile changed graph semantics.");
            Require(baseline == Verify(second, profile), "Fresh declaration changed graph semantics.");
            first.Dispose();
            Require(!first.IsBuilding, "Disposed builder still accepts declarations.");
            bool rejected = false;
            try
            {
                using CompiledRenderGraph unexpected = first.Compile();
            }
            catch (ObjectDisposedException)
            {
                rejected = true;
            }

            Require(rejected, "Disposed graph compiled successfully.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = "G04",
                version = 1,
                profile.Id,
                profile.Shape,
                declaredPasses = profile.Passes,
                livePasses = profile.Live,
                culledPasses = profile.Unused,
                unusedPercent = profile.UnusedPercent,
                resources = profile.Passes * profile.Lanes,
                outputResources = profile.Outputs * profile.Lanes,
                dependencies = profile.Edges,
                barriers = ((2 * profile.Live) - profile.Outputs) * profile.Lanes,
                dimensions = "16x16 Rgba8Unorm, one mip/layer",
                backend = "Vulkan device prerequisite only; no Record/allocation/submit",
                status = "passed",
            }));
        }
    }

    internal static string Verify(Graph graph, GraphProfile profile)
    {
        using CompiledRenderGraph plan = graph.Compile(capture: new RenderGraphCaptureOptions());
        var expected = new RenderGraphStatistics(profile.Live, profile.Unused, profile.Passes * profile.Lanes, profile.Passes * profile.Lanes, profile.Edges, ((2 * profile.Live) - profile.Outputs) * profile.Lanes);
        Require(plan.Statistics == expected, $"Statistics mismatch: {profile.Id}: {plan.Statistics} != {expected}");
        Require(plan.PassNames.SequenceEqual(Enumerable.Range(0, profile.Live).Select(index => $"pass-{index:D3}")), "Live pass order mismatch.");
        Require(plan.CulledPassNames.SequenceEqual(Enumerable.Range(profile.Live, profile.Unused).Select(index => $"pass-{index:D3}")), "Culled pass list mismatch.");
        RenderGraphSnapshot snapshot = plan.Snapshot ?? throw new InvalidOperationException("Capture exceeded bounded smoke budget.");
        Require(snapshot.Passes.Count == profile.Passes && snapshot.Resources.Count == expected.Resources, "Snapshot counts mismatch.");
        for (int index = 0; index < profile.Passes; index++)
        {
            Require(snapshot.Passes[index] == new RenderGraphPassInfo($"pass-{index:D3}", index < profile.Live ? index : -1, false), "Pass metadata mismatch.");
            bool live = index < profile.Live;
            bool output = live && index >= profile.Live - profile.Outputs;
            int last = index;
            if (live && !output)
            {
                last = profile.Shape switch
                {
                    "chain" => index + 1,
                    "multioutput" => index + 4,
                    "fanout" => index == 0 ? profile.Live - 2 : profile.Live - 1,
                    _ => throw new InvalidOperationException("Unknown shape."),
                };
            }

            for (int lane = 0; lane < profile.Lanes; lane++)
            {
                int resourceIndex = (index * profile.Lanes) + lane;
                var resource = new RenderGraphResourceInfo($"texture-{resourceIndex:D3}", "Texture", true, output, live ? index : -1, live ? last : -1);
                Require(snapshot.Resources[resourceIndex] == resource, $"Resource lifetime mismatch: {resource.Name}");
            }
        }

        var edges = new HashSet<RenderGraphDependencyInfo>();
        for (int after = 1; after < profile.Live; after++)
        {
            if (profile.Shape == "chain")
            {
                edges.Add(new(after - 1, after));
            }
            else if (profile.Shape == "multioutput" && after >= 4)
            {
                edges.Add(new(after - 4, after));
            }
            else if (profile.Shape == "fanout")
            {
                if (after == profile.Live - 1)
                {
                    for (int branch = 1; branch < profile.Live - 1; branch++)
                    {
                        edges.Add(new(branch, after));
                    }
                }
                else
                {
                    edges.Add(new(0, after));
                }
            }
        }

        Require(edges.Count == profile.Edges && edges.SetEquals(snapshot.Dependencies), "Exact dependency edges mismatch.");

        // Run GUIDs intentionally excluded: graph/compile identities are nondeterministic by contract.
        return JsonSerializer.Serialize(new { plan.Statistics, plan.PassNames, plan.CulledPassNames, snapshot.Passes, snapshot.Resources, snapshot.Dependencies, snapshot.Barriers });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
