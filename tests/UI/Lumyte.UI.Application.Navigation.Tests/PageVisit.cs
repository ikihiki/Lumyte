namespace Lumyte.UI.Application.Navigation.Tests;

internal sealed class PageVisit(UiPageContext context, PageService service)
{
    internal UiPageContext Context { get; } = context;

    internal PageService Service { get; } = service;

    internal int Prepares { get; set; }

    internal int Builds { get; set; }

    internal int Retires { get; set; }

    internal int Disposes { get; set; }

    internal bool RetiredOnOwner { get; set; }
}
