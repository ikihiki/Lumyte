using System.Text.Json.Nodes;

namespace Lumyte.Build.Tests;

internal static class ScreeningFixtureExtensions
{
    public static JsonObject WithMeasurements(this JsonObject fixture)
    {
        for (int i = 0; i < 8; i++)
        {
            fixture["Benchmarks"]![0]!["Measurements"]!.AsArray().Add(JsonNode.Parse("""{"IterationStage":"Result","IterationMode":"Workload","Nanoseconds":1000,"Operations":100}"""));
        }

        return fixture;
    }
}
