using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target ComponentScreeningNotify => definition => definition
        .Executes(async () =>
        {
            string result = await ComponentScreeningNotification.PublishFromEnvironmentAsync().ConfigureAwait(false);
            Console.WriteLine(result);
        });
}
