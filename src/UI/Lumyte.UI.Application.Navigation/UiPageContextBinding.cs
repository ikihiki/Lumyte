namespace Lumyte.UI.Application.Navigation;

internal sealed class UiPageContextBinding
{
    private UiPageContext? _context;

    internal UiPageContext Context => _context ?? throw new InvalidOperationException("The page scope has not been bound to an activation.");

    internal void Bind(UiPageContext context)
    {
        if (_context is not null)
        {
            throw new InvalidOperationException("The page scope is already bound.");
        }

        _context = context;
    }
}
