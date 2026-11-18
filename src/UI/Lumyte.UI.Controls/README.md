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

ScrollBarは既存のValueで現在位置を上限に対する0〜1の比率として公開する。RoleはScrollBarで、値の設定やClick capabilityを公開しない。操作は通常のpointer/key入力を使う。

## Slider・RangeSlider

有限なMin/Max/Stepと受動的なValueを指定する。受理値はSliderElement.ValueまたはRangeSliderElement.Valueにあり、新しい宣言の採用だけが静かに置き換える。RangeSliderの値はSliderRangeで原子的に通知する。Spaceで操作するつまみを切り替え、矢印・PageUp/PageDown・Home/Endを使用する。取消は最後の受理値を維持し、正常なpointer/key終端だけOnCommittedを通知する。

SetValueAsync/SetRangeAsyncは通常入力leaseと現在のprobeを使う。NumericBoxは本packageではなくTextEditingの実editorである。[詳細な判断](../../../docs/adr/ui/UI-0031-numeric-input-and-accepted-values.md)と`samples/Lumyte.UI.Numeric.Sample`を参照する。

## 保持タブと折り畳み

TabViewの通常内容は非空・ordinal一意の継承Keyを持つ直属TabItemだけを受け付けます。
HeaderとContentは既存の生成named slotで各0〜1個です。通常内容は空にします。

```csharp
using Lumyte.UI.Controls;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

var tabs = TabView(selectedKey: "a")[
    TabItem(key: "a")[
        TabItem.Header()[Rectangle(width: 120, height: 24)],
        TabItem.Content()[Expander(isExpanded: true)[
            Expander.Header()[Rectangle(width: 120, height: 24)],
            Expander.Content()[Rectangle(width: 160, height: 80)]
        ]]
    ]
];
```

TabViewは左の縦見出し列と右の選択内容を一回ずつ配置します。HeaderWidthは既定120、HeaderHeightは既定24です。
非選択・折り畳み内容のWidget、Element、文書と購読は維持し、layout・描画・hit・focus・IMEから除外します。
受理したSelectedKeyとIsExpandedはsurfaceごとに独立し、ユーザー変更を先に確定して最新callbackを一回呼びます。
新しい宣言の値は通知なしに採用し、Signalへ暗黙に書き戻しません。未知の選択keyはnullになります。
矢印・Home・Endは有効見出しへfocusと選択を移し、明示focusだけでは選択しません。
見出し内のButton等は自身の操作を所有し、親の操作を重複実行しません。

AutomationはTabViewのSelectOptionAsync、TabItemのClick、ExpanderのExpandAsync／CollapseAsyncを使えます。
後者は受理値を検査する冪等な通常入力であり、CheckBoxの偽装ではありません。
[公開Headlessサンプル](../../../samples/Lumyte.UI.Tabs.Sample/README.md)は明示fontと実際の編集接続を使います。

## Menu・MenuItem・Tooltip

Menuは、非空・ordinal一意の継承Keyを持つ直属MenuItemと既存Separatorを縦に並べます。
MenuItemの通常内容は0〜1個の表示ラベルです。内部Buttonを追加せず、MenuItem自身がClickとfocusを持ちます。
上下・Home・Endで有効な項目へ移動し、Rightでsubmenuを開き、Enter・Spaceで一回だけ操作します。
IsCheckableを指定した項目だけがboolのIsCheckedとCheck操作を公開します。受理値とsubmenu状態はsurfaceごとに独立します。

```csharp
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

var menu = Menu(popupHost: host)[
    MenuItem(key: "save", onInvoke: Save)[Rectangle(width: 160, height: 24)],
    Separator(),
    MenuItem(key: "check", isCheckable: true, onChanged: SaveChecked)[Rectangle(width: 160, height: 24)],
    MenuItem(key: "more", createSubmenu: CreateFreshMenu)[Rectangle(width: 160, height: 24)]
];
```

hostは任意packageのMenuPopupHostを借用します。ルート表示は既存Windowing.Compose.Popupの内容にMenuを置き、OnDismissでアプリの表示モデルを更新します。
commandのOnInvokeとCreateSubmenu、またはチェックとsubmenuの併用は拒否します。新しい宣言の値は通知なしに採用し、入力がWidgetやSignalを暗黙に変更することはありません。

Tooltipは0〜1個の通常アンカーと、開くたびに新しいツリーを返すCreatePopupを持ちます。
IsOpenは明示的な表示モデルで、hover／focusから値を作るtriggerやtimerは含みません。
Passive popupの表示・終了はeditor focus、preedit、pointer、wheelを奪わず、子の入力対象とautomation IDを保ちます。
新しい宣言の採用・取り外し・実効非表示で古い要求を取消します。factoryの失敗でも既存menuは維持します。

[公開Headlessサンプル](../../../samples/Lumyte.UI.Menus.Sample/README.md)は明示font、通常popup入力と実編集接続を使います。
判断は[UI-0035](../../../docs/adr/ui/UI-0035-menu-and-passive-tooltip.md)を参照してください。

## Stateful visual slots

`Slider()[Slider.Thumb()[thumb => Media.Text(value: thumb.Value.ToString(), layoutProvider: textProvider)]]`
uses the accepted per-element value, not the declaration's initial value.
Slider.Track() and Slider.Thumb() accept CompositionTemplate<SliderThumbContext, Widget>
or a fixed Widget. RangeSlider.Track(), Thumb(), LowerThumb(), and UpperThumb()
accept CompositionTemplate<RangeSliderThumbContext, Widget> or a fixed Widget;
IsUpper identifies the thumb (Track receives the lower value and false).
A specific LowerThumb/UpperThumb overrides the shared Thumb template for that part.

Templates run at mount, configuration adoption, and queued idle updates after
accepted state changes, not during drawing or input delivery. Multiple pending
changes may coalesce. A template exception propagates; its previous visual tree
stays published and the refresh remains retryable.

The original track and fixed 10-unit thumb rectangles are retained. Missing parts
use built-in drawing. Decoration descendants cannot receive input, focus, or
automation exposure; the control remains the gesture/capture/automation owner.
Templates must return parentless roots with disjoint descendants. A fixed Widget
may remain on its original mounted part, but cannot be shared across parts,
surfaces, or mounted lifetimes. Use factories for independently mounted controls.

Switch/selection-indicator templates remain deferred: those controls must preserve
their ordinary content and background slots, unlike the numeric controls' owned
visual-only subtree. ScrollBar templates are deferred pending controller-driven
refresh integration rather than polling or executing templates while drawing.
