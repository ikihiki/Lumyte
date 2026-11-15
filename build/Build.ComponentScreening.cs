using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Exact base revision for advisory screening.")]
    public string? ScreeningBase { get; set; }

    [Parameter("Exact head revision for advisory screening.")]
    public string? ScreeningHead { get; set; }

    [Parameter("Frozen screening plan JSON path.")]
    public string? ScreeningPlan { get; set; }

    [Parameter("Optional read-only base checkout; HEAD must match the plan.")]
    public string? ScreeningBaseDirectory { get; set; }

    [Parameter("Optional read-only head checkout; HEAD must match the plan.")]
    public string? ScreeningHeadDirectory { get; set; }

    [Parameter("Select all twelve representative cases, including A/A.")]
    public bool ScreeningAll { get; set; }

    [Parameter("Total screening deadline in seconds, capped at 900.")]
    public double ScreeningTimeoutSeconds { get; set; } = 900;

    private string ScreeningOutput => Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, "component-screening"));

    private Target ComponentScreeningPlan => definition => definition.Executes(async () =>
    {
        await ComponentScreening.CreatePlanAsync(RepositoryDirectory, ScreeningBase ?? throw new ArgumentException("--screening-base is required"),
            ScreeningHead ?? throw new ArgumentException("--screening-head is required"), ScreeningOutput, ScreeningAll).ConfigureAwait(false);
    });

    private Target ComponentScreeningRun => definition => definition.Executes(async () =>
    {
        bool success = await ComponentScreening.RunAsync(RepositoryDirectory, ScreeningPlan ?? Path.Combine(ScreeningOutput, "plan.json"),
            ScreeningOutput, DotNetExecutable, ScreeningTimeoutSeconds, ScreeningBaseDirectory, ScreeningHeadDirectory).ConfigureAwait(false);
        if (!success)
        {
            throw new InvalidOperationException("Component screening failed; inspect result.json and summary.md.");
        }
    });
}
