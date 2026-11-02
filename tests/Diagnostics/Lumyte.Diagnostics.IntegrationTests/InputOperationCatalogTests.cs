using Lumyte.Diagnostics.Server.Components;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Protects generic Input forms independently of version-two snapshot parsing.</summary>
public sealed class InputOperationCatalogTests
{
    /// <summary>Checks only supported Input schemas publish generic operation forms.</summary>
    /// <param name="version">The advertised Input schema version.</param>
    /// <param name="supported">Whether generic forms are supported.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void SupportedVersionsPreservePublishedOperations(int version, bool supported)
    {
        var operation = new OperationDescriptor("override-button", "Override button", DiagnosticPermission.OverrideInput, [], []);
        var snapshot = new OperationDescriptor("get-snapshot", "Snapshot", DiagnosticPermission.Observe, [], []);
        var subsystem = new SubsystemDescriptor("input", "Input", version);
        DiagnosticSubsystemCatalog[] catalog = [new(subsystem, [operation, snapshot]), new(new("other", "Other", 1), [operation])];

        (SubsystemDescriptor Subsystem, OperationDescriptor Operation)[] forms = InputPage.GetInputOperations(catalog).ToArray();
        if (supported)
        {
            Assert.Equal((subsystem, operation), Assert.Single(forms));
        }
        else
        {
            Assert.Empty(forms);
        }
    }

    /// <summary>Checks disconnected resources expose no generic forms.</summary>
    [Fact]
    public void EmptyCatalogHasNoForms() => Assert.Empty(InputPage.GetInputOperations([]));
}
