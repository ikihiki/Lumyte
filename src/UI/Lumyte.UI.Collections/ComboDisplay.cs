using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class ComboDisplay(Func<Widget> build) : UiFunctionWidget
{
    protected override Widget Build() => build();

    protected override Widget? RebuildDeclaration(UiWidgetRebuildContext context) => new ComboDisplay(build);
}
