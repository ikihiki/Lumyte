using System.Runtime.Versioning;
using Lumyte.UI.BrowserBenchmarkHost;

[assembly: SupportedOSPlatform("browser")]

await BrowserWorkload.RunAsync();
