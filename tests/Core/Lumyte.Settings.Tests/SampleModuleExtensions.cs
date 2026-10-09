using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lumyte.Settings.Tests;

internal static class SampleModuleExtensions
{
    public static IServiceCollection UseSampleModule(this IServiceCollection services)
    {
        services.AddPersistedOptions<SampleSettings>("sample").UseJsonDefinition<SampleSettings, SampleDefinition>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SampleSettings>, SampleValidator>());
        return services;
    }

    private sealed class SampleValidator : IValidateOptions<SampleSettings>
    {
        public ValidateOptionsResult Validate(string? name, SampleSettings options)
        {
            if (name is not null && name != Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            var errors = new List<string>();
            CheckRange(options.PrimaryRange, nameof(options.PrimaryRange), errors);
            CheckRange(options.SecondaryRange, nameof(options.SecondaryRange), errors);
            if (options.Entries is null)
            {
                errors.Add("Entries cannot be null.");
            }

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        private static void CheckRange(SampleRange? range, string path, List<string> errors)
        {
            if (range is null || !float.IsFinite(range.Minimum) || !float.IsFinite(range.Maximum) || range.Minimum < 0 || range.Minimum >= range.Maximum || range.Maximum > 1)
            {
                errors.Add($"{path}: Expected finite 0 <= Minimum < Maximum <= 1.");
            }
        }
    }
}
