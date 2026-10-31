using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Restored Microsoft.Direct3D.D3D12 package directory containing the selected Agility SDK.")]
    public string? AgilitySdkRoot { get; set; }

    [Parameter("Agility SDK channel: Stable (default) or Preview.")]
    public string AgilityChannel { get; set; } = Environment.GetEnvironmentVariable("LumyteAgilityChannel") ?? "Stable";

    private string SelectedAgilityChannel => AgilityChannel.ToUpperInvariant() switch
    {
        "STABLE" => "Stable",
        "PREVIEW" => "Preview",
        _ => throw new ArgumentException("Agility channel must be Stable or Preview."),
    };

    private Target Direct3D12Native => definition => definition
        .Executes(async () =>
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Direct3D 12 native builds require Windows.");
            }

            string output = Path.GetFullPath(Output ?? Path.Combine(RepositoryDirectory, "artifacts", "direct3d12", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, SelectedAgilityChannel, Configuration));
            ArgumentException.ThrowIfNullOrWhiteSpace(AgilitySdkRoot);
            await ProcessRunner.RunAsync(FindExecutable("cmake"), ["-S", Path.Combine(RepositoryDirectory, "native", "lumyte_graphics_d3d12"), "-B", output, "-G", "Ninja", $"-DCMAKE_BUILD_TYPE={Configuration}", $"-DAGILITY_SDK_ROOT={Path.GetFullPath(AgilitySdkRoot)}", $"-DAGILITY_CHANNEL={SelectedAgilityChannel}"], RepositoryDirectory).ConfigureAwait(false);
            await ProcessRunner.RunAsync(FindExecutable("cmake"), ["--build", output, "--parallel", "2"], RepositoryDirectory).ConfigureAwait(false);
        });
}
