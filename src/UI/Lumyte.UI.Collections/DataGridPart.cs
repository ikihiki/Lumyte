using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class DataGridPart(Func<Widget> build) : UiFunctionWidget
{
    protected override Widget Build() => build();

    protected override Widget RebuildDeclaration(UiWidgetRebuildContext context) => new DataGridPart(build);
}
