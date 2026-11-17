using System.Reflection;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lumyte.Testing;

// Validate the full, unfiltered xUnit discovery stream in the existing SDK test
// invocation. No source scanning, extra test process, or hand-maintained case list.
internal sealed class LaneInventoryDiscoverer(ITestFrameworkDiscoverer inner, IAssemblyInfo assembly) : LongLivedMarshalByRefObject, ITestFrameworkDiscoverer
{
    public string TargetFramework => inner.TargetFramework;

    public string TestFrameworkDisplayName => inner.TestFrameworkDisplayName;

    public string Serialize(ITestCase testCase) => inner.Serialize(testCase);

    public void Dispose() => inner.Dispose();

    public void Find(bool includeSourceInformation, IMessageSink messageSink, ITestFrameworkDiscoveryOptions discoveryOptions)
        => inner.Find(includeSourceInformation, Wrap(messageSink), discoveryOptions);

    // IDE single-type discovery is intentionally partial, not an inventory audit.
    public void Find(string typeName, bool includeSourceInformation, IMessageSink messageSink, ITestFrameworkDiscoveryOptions discoveryOptions)
        => inner.Find(typeName, includeSourceInformation, messageSink, discoveryOptions);

    private IMessageSink Wrap(IMessageSink sink)
    {
        string? catalog = Environment.GetEnvironmentVariable("LUMYTE_LANE_CATALOG");
        return catalog is null || Environment.GetEnvironmentVariable("LUMYTE_LANE_INVENTORY") is null
            ? sink : new InventorySink(sink, assembly, catalog);
    }

    private sealed class InventorySink(IMessageSink sink, IAssemblyInfo assembly, string catalog) : LongLivedMarshalByRefObject, IMessageSink
    {
        private readonly HashSet<string> _lanes = new(StringComparer.Ordinal);
        private readonly JsonDocument _document = JsonDocument.Parse(File.ReadAllText(catalog));

        private JsonElement Entry => _document.RootElement.EnumerateArray().Single(entry => Path.GetFullPath(entry.GetProperty("Project").GetString()!, Path.GetDirectoryName(Path.GetDirectoryName(catalog))!) == Project);

        private string Project => assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute)).OfType<IReflectionAttributeInfo>()
            .Select(attribute => attribute.Attribute).OfType<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "Lumyte.TestProjectPath").Value!;

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage discovered)
            {
                string[] traits = discovered.TestCase.Traits.Where(pair => pair.Key.Equals("Execution", StringComparison.OrdinalIgnoreCase)).SelectMany(pair => pair.Value).ToArray();
                string name = discovered.TestCase.TestMethod.TestClass.Class.Name.Split('.').Last();
                bool external = Entry.GetProperty("IntegrationClasses").EnumerateArray().Any(value => value.GetString() == name);
                _lanes.Add(traits.Contains("Gpu", StringComparer.Ordinal) ? "Gpu"
                    : traits.Contains("Integration", StringComparer.Ordinal) || external ? "Integration"
                    : Entry.GetProperty("Default").GetString()!);
            }

            if (message is IDiscoveryCompleteMessage)
            {
                string? error = Validate();
                string directory = Environment.GetEnvironmentVariable("LUMYTE_LANE_INVENTORY")!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, Path.GetFileName(Project) + ".json"), JsonSerializer.Serialize(new { Project, Lanes = _lanes.Order().ToArray(), Error = error }));
                _document.Dispose();
                if (error is not null)
                {
                    sink.OnMessage(new ErrorMessage(Array.Empty<ITestCase>(), new InvalidDataException(error)));
                }
            }

            return sink.OnMessage(message);
        }

        private string? Validate()
        {
            JsonElement entry = Entry;
            string[] expected = entry.GetProperty("Lanes").EnumerateArray().Select(value => value.GetString()!).ToArray();

            // IntegrationClasses can contain source excluded on this host (for
            // example WindowsNativeTests on Linux). Validate actual lane presence,
            // not that every cross-platform class is discoverable on every host.
            return !_lanes.SetEquals(expected)
                ? $"Lane inventory drift for {Project}. Update build/TestLanes.json: discovered lanes [{string.Join(", ", _lanes.Order())}], declared [{string.Join(", ", expected)}]."
                : null;
        }
    }
}
