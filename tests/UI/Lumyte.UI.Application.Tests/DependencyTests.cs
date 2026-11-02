using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Protects the standard host's optional navigation boundary.</summary>
public sealed class DependencyTests
{
    /// <summary>Checks the host, headless driver and basic diagnostics carry no page navigation assembly references.</summary>
    [Fact]
    public void StandardApplicationAndDiagnosticsDoNotReferenceNavigation()
    {
        Type[] types = [typeof(UiApplication), typeof(UiApplicationDiagnosticsExtensions), typeof(UiHeadlessApplicationExtensions)];
        foreach (Type type in types)
        {
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(), reference => reference.Name?.Contains("Navigation", StringComparison.Ordinal) == true);
        }
    }
}
