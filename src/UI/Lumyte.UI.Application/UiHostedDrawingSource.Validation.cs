namespace Lumyte.UI.Application;

/// <summary>Applies pending declarations before checking mounted component requirements.</summary>
internal sealed partial class UiHostedDrawingSource
{
    private void PrepareSurface(UiSurface surface, bool retained = false)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (retained)
            {
                surface.ValidateRetainedDrawingDependencies();
            }

            surface.UpdateLayout();
            if (ValidateSurface is { } validate)
            {
                validate(surface);
            }
            else
            {
                ValidateRoot?.Invoke(surface.Root);
            }

            if (!surface.HasPendingFrameUpdates)
            {
                return;
            }
        }

        throw new InvalidOperationException("Surface validation did not converge.");
    }
}
