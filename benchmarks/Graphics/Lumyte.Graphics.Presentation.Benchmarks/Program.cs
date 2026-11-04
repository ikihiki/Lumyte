using System.Text.Json;

namespace Lumyte.Graphics.Presentation.Benchmarks;

internal static class Program
{
    internal static void Emit<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private static int Main(string[] args)
    {
        bool optionsValidated = false;
        try
        {
            // No platform/device/native call occurs until every option has been validated.
            var options = Options.Parse(args);
            optionsValidated = true;
            if (options.Command == "--list")
            {
                Console.WriteLine("forced-clear: real X11/WebGPU presentation plumbing, no UI workload");
                Console.WriteLine("u03-forced-draw: real mounted UiScenario U03/native text/Renderer2D presentation");
                Console.WriteLine("benchmark-idle-u03: same real U03 content; benchmark-owned stop/resume, NOT product host proof");
                return 0;
            }

            if (options.Command == "--validate-options")
            {
                Emit(new { status = "options-valid", options });
                return 0;
            }

            if (options.Command == "--check-schedule")
            {
                DemandSchedule.Check();
                LifetimeChecks.Check();
                Emit(new { status = "managed-policy-check-passed", retirementOwnershipChecked = true, productHostProof = false, gui = false });
                return 0;
            }

            if (options.Command == "--check-pixels")
            {
                if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
                {
                    Emit(new { status = "blocked", reason = "Headless WebGPU gate requires 64-bit Linux; no window/device created." });
                    return 3;
                }

                return HeadlessPixelRunner.Run(options);
            }

            if (!OperatingSystem.IsLinux() || IntPtr.Size != 8 || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                Emit(new { status = "blocked", reason = "Authorized 64-bit Linux X11 DISPLAY required, physical or explicitly classified isolated virtual. Wayland-only is unsupported. No device/window was created." });
                return 3;
            }

            if (options.Command == "--probe-product-host")
            {
                return ProductHostProbe.Run(options.IdleMs, options.DisplayKind);
            }

            return PresentationRunner.Run(options);
        }
        catch (ArgumentException error) when (!optionsValidated)
        {
            Emit(new { status = "invalid-options", reason = error.Message });
            return 2;
        }
        catch (Exception error)
        {
            Emit(new { status = "failed", reason = error.ToString() });
            return 1;
        }
    }
}
