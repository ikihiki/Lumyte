using Xunit;

namespace Lumyte.Audio.OpenAL.Tests;

/// <summary>Runs native audio regressions only when OpenAL Soft is explicitly enabled.</summary>
public sealed class OpenALFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="OpenALFactAttribute"/> class.</summary>
    public OpenALFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_OPENAL_TESTS") != "1")
        {
            Skip = "Set LUMYTE_OPENAL_TESTS=1 with OpenAL Soft installed.";
        }
    }
}
