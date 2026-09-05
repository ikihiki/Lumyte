namespace Lumyte.DevTools.Server.Tests;

public sealed class DevToolsAssemblyBoundaryTests
{
    [Fact]
    public void ServerDependsOnProtocolWithoutDependingOnAgentRuntime()
    {
        string[] references = typeof(DevToolsHostRegistry).Assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Lumyte.DevTools.Protocol", references);
        Assert.DoesNotContain("Lumyte.DevTools.Agent", references);
    }
}
