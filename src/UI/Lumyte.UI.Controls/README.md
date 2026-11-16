# Lumyte.UI.Controls

Optional typed ToggleButton, CheckBox, RadioButton, RadioGroup and Switch controls.

```csharp
using static Lumyte.UI.Controls.Compose;

var enabled = Switch(isOn: false, automationName: "Sound", onChanged: value => Save(value));
var quality = RadioGroup(selectedKey: "balanced")[
    RadioButton(key: "balanced", automationName: "Balanced"),
    RadioButton(key: "high", automationName: "High")];
```

Values belong to mounted elements; declarations remain passive and shareable. A new declaration adoption explicitly replaces the accepted value without notifying. User changes commit before the latest callback; absent handlers still allow local operation. Write a Signal model in the callback to share or persist values.

CheckBox cycles false → true → false, or false → true → mixed → false with ThreeState. A mixed two-state declaration becomes true on activation. RadioButton uses its inherited init-only Key, requires a direct RadioGroup parent and derives selection from the group's single ordinal key. Missing selection resolves to null without callback. Enabled visible options support arrows and one roving Tab stop.

Background is a named content slot; TrackColor and Foreground are explicit sRGB colors. MarkSize and Gap are finite nonnegative values. Marks load no font or service. Supply optional Media.Text content with an explicit provider. Background ordering uses the existing conservative opaque retained path; destination capture reuse is disabled for aspect-dependent marks.

Input uses normal surface dispatch and the same Button recognizer. CheckAsync/UncheckAsync dispatch a single desired-state input after normal capability, geometry and probe validation; SelectOptionAsync activates the radio through normal pointer input. Internal marks add no focus or semantic targets. Controls register no TextInputScope handler.

See the public Headless settings sample in `samples/Lumyte.UI.Controls.Sample` and the detailed decision in [UI-0028](../../../docs/adr/ui/UI-0028-selection-controls-and-shared-activation.md).

## ScrollBar

ScrollBarはScrollControllerを借用し、thumb drag、track page、矢印/PageUp/PageDown/Home/Endで同じoffsetを操作する。
配置には通常のGrid.Cell/Flex.Flexibleを使い、内部Buttonや追加のautomation IDは作らない。色は引数で明示できる。
