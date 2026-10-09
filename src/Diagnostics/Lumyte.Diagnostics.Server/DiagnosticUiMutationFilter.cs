using Microsoft.AspNetCore.Antiforgery;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticUiMutationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        return await next(context).ConfigureAwait(false);
    }
}
