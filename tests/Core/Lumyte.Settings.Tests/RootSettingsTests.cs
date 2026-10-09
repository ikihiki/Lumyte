using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Checks the model-class constraint at module registration.</summary>
public sealed class RootSettingsTests
{
    /// <summary>Rejected roots leave existing registrations intact and their section IDs available.</summary>
    /// <param name="withExistingModule">Whether another module has already registered.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionRootsAreRejectedWithoutChangingRegistrations(bool withExistingModule)
    {
        var services = new ServiceCollection();
        if (withExistingModule)
        {
            services.AddPersistedOptions<SampleSettings>("existing");
        }

        ServiceDescriptor[] before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<List<int>>("module"));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<Dictionary<string, int>>("module"));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<DerivedList>("module"));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<StringCollection>("module"));
        Assert.Equal(before, services.ToArray());

        services.AddPersistedOptions<CollectionSettings>("module");
        if (withExistingModule)
        {
            services.AddPersistedOptions<SampleSettings>("existing");
        }
    }

    /// <summary>A collection subclass must not become a supported root merely by being a class.</summary>
    public sealed class DerivedList : List<int>;
}
