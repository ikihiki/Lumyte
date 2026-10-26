using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Checks that automatic copying retains dictionary lookup semantics.</summary>
public sealed class DictionaryCopyTests
{
    /// <summary>Generated and reflection metadata both retain comparers and isolate values.</summary>
    /// <param name="generatedMetadata">Whether to use generated serialization metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticCopyPreservesComparersAndIsolatesEntries(bool generatedMetadata)
    {
        var services = new ServiceCollection();
        services.AddPersistedOptions<DictionaryCopySettings>("dictionary");
        if (generatedMetadata)
        {
            services.AddPersistedOptions<DictionaryCopySettings>("dictionary").UseJsonTypeInfo(DictionaryCopyJsonContext.Default.DictionaryCopySettings);
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        ISettingsDefinition<DictionaryCopySettings> definition = provider.GetRequiredService<ISettingsDefinition<DictionaryCopySettings>>();
        var customComparer = new HyphenIgnoringComparer();
        var original = new DictionaryCopySettings
        {
            Entries = new(StringComparer.OrdinalIgnoreCase) { ["MixedCase"] = new SampleRange { Minimum = float.NaN } },
            Counts = new(customComparer) { ["with-hyphen"] = 7 },
        };

        DictionaryCopySettings copy = definition.DeepClone(original);
        Assert.Same(original.Entries.Comparer, copy.Entries.Comparer);
        Assert.Same(customComparer, copy.Counts.Comparer);
        Assert.True(float.IsNaN(copy.Entries["mixedcase"].Minimum));
        Assert.Equal(7, copy.Counts["withhyphen"]);
        copy.Entries["MIXEDCASE"].Minimum = 0.9f;
        copy.Entries["new"] = new();
        copy.Counts["withhyphen"] = 8;
        Assert.True(float.IsNaN(original.Entries["MixedCase"].Minimum));
        Assert.False(original.Entries.ContainsKey("new"));
        Assert.Equal(7, original.Counts["with-hyphen"]);
    }

    private sealed class HyphenIgnoringComparer : IEqualityComparer<string>
    {
        public bool Equals(string? left, string? right) => Normalize(left) == Normalize(right);

        public int GetHashCode(string value) => StringComparer.Ordinal.GetHashCode(Normalize(value)!);

        private static string? Normalize(string? value) => value?.Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
