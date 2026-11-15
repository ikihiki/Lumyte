using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target GenerateNamedColors => definition => definition
        .DependsOn(Compile)
        .Executes(() => RunNamedColorsAsync(false));

    private Target VerifyNamedColors => definition => definition
        .DependsOn(Compile)
        .Executes(() => RunNamedColorsAsync(true));

    private async Task RunNamedColorsAsync(bool verify)
    {
        const string Tool = "tools/colors/Lumyte.NamedColors.Generator/Lumyte.NamedColors.Generator.csproj";
        await DotNetAsync("build", Tool, "--configuration", Configuration, "-m:1").ConfigureAwait(false);
        string assembly = Path.Combine("tools", "colors", "Lumyte.NamedColors.Generator", "bin", Configuration, "net10.0", "Lumyte.NamedColors.Generator.dll");
        await DotNetAsync(verify ? [assembly, "--verify"] : [assembly]).ConfigureAwait(false);
    }
}
