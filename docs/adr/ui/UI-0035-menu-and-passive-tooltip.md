# ADR-UI-0035: 通常入力で操作するメニューと受動ツールチップ

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `6fa8980717d7404ebbca459987accb74355dbc8b`
- 変更関係: 一部変更
- 変更元: [UI-0013の宣言的なMenuと用途別Popup](UI-0013-component-catalog-and-delivery-plan.md#宣言的なmenuと用途別popup)、[UI-0011の開く・失敗・終了の順序](UI-0011-popup-windowing-and-fallback.md#開く失敗終了の順序)
- 変更対象: Menuの表示用追加factory候補を既存Popupと内容Menuへ統合し、内容の新規性検証、取消した置換の抑止と新しいfocus要求の優先を追加する。
- 継続する判断: 三部品の責務、commandとsubmenuの排他、宣言的な閉鎖要求、PopupとIMEの分離を維持する。

## 背景

アプリの作成者は、既存PopupへButtonを並べるだけではメニュー項目のチェック、型付きの子制約、submenuと通常入力の意味情報を共有できない。基準コミットの`src/UI/Lumyte.UI.Windowing/UiPopupManager.cs`は表示と入力系列を既に管理するため、新しいWindow管理ではなく内容部品を追加する。

## 決定

ControlsへMenu、MenuItem、Tooltipを追加し、表示は借用した小さなpopup契約を介して既存UiPopupManagerへ委譲する。Menuの表示宣言は既存Popupをそのまま使い、同じ役割の追加表示factoryやContextMenu型を設けない。Tooltipは明示的なIsOpenを正本とし、hoverやfocusからその値を作るアプリの状態更新と表示の寿命を分離する。

### 利用者の変更前・変更後

変更前は既存の`Lumyte.UI.Windowing.Compose.Popup`へButtonを置けるが、MenuItemの型制約とチェック操作はない。以下の両例ではhost、表示状態、保存処理をアプリが所有し、文字描画の代わりに既存Rectangleを使用する。

```csharp
// 変更前。using static Lumyte.UI.Compose;
// using static Lumyte.UI.Windowing.Compose;
Popup(key: "file", anchor: "open", isOpen: open)[
    Button(onClick: Save)[Rectangle(width: 80, height: 24)]
];
```

```csharp
// 変更後。using static Lumyte.UI.Compose;
// using static Lumyte.UI.Controls.Compose;
// using static Lumyte.UI.Windowing.Compose;
Popup(key: "file", anchor: "open", isOpen: open, onDismiss: Dismiss)[
    Menu(popupHost: host)[
        MenuItem(key: "save", onInvoke: Save)[Rectangle(width: 80, height: 24)]
    ]
];
Tooltip(popupHost: host, isOpen: helpOpen, createPopup: CreateHelp)[
    Rectangle(width: 80, height: 24)
];
```

`host`はControls.WindowingのMenuPopupHostであり、アプリがmanagerとowner登録を借用して作る。通常の公開HeadlessUiApplicationでは既存のPopup調停とowner loopを利用する。Tooltipはアンカー内容を零個または一個持ち、零個なら自身の明示寸法をアンカーとする。初期範囲では自動hover、遅延、thread timerを追加せず、IsOpenの更新に反応して表示・取消する。

### 項目と入力の契約

Menuの通常直下は実際のMenuItem型と既存Separator型だけとし、Separatorへのfocus指定は拒否する。MenuItemは非空の継承Keyを持ち、Menu内でordinal比較によって一意とする。宣言検査は投影されたツリー上で行い、不正な更新では公開前に失敗して既存ツリーを保つ。意味roleの書換えは型判定や操作能力の代用にならない。

MenuItemはOnInvokeとCreateSubmenuの同時指定を拒否する。チェック項目はIsCheckable、boolのIsChecked、OnChangedを明示し、submenuとの併用を拒否する。受理したチェックとsubmenuの印は明示的なForeground色で描き、追加の入力対象を作らない。値はElementごとに受理し、入力は宣言やSignalを書き換えない。新宣言の採用は値を黙って更新し、旧入力系列とsubmenuを取消す。同じ宣言を二つのsurfaceへ置いても値と表示要求を共有しない。

Menuは一つの項目だけをTab候補にする。上下、Home、Endは非表示・無効な項目を飛ばしてfocusを動かし、repeatとUpまで系列を消費する。EnterとSpaceは共有UiActivationRecognizerで一回だけ認識する。Rightはsubmenuを開き、LeftとEscapeのpopup閉鎖は既存managerが担う。内部Buttonを重ねず、項目自体をClick、チェック可能な項目だけをCheck、submenuだけをExpandCollapseの操作対象とする。

命令項目は受理値を確定し、chainの閉鎖要求を先にqueueしてから最新のcallbackを一回呼ぶ。callbackが例外を投げても閉鎖要求は残る。宣言的PopupではItemInvokedを既存OnDismissへqueueし、モデルがtrueのままなら表示を維持する。命令的Popupではchainを閉じる。閉鎖時は、開いた時点より新しい親surfaceのfocus要求があれば旧bookmarkを復帰しない。

### popupの寿命と失敗

MenuItemとTooltipはhostを借用し、要求tokenのみを所有する。Openを呼ぶ前に世代付き予約を設け、再入、宣言更新、取り外し、surface破棄による取消後は遅れたhandleを直ちに閉じる。利用者factoryはmanagerのowner loopで一回だけ実行し、同じopenのfallbackで再実行しない。返却rootと実際の関数展開を含む子孫は、新規・未接続・他surfaceと非共有であることを公開前に検査する。このadapterは内容の準備を既存menuの置換より先に行い、失敗で既存親menuを破棄しない。任意のfactoryの後と置換要求の受理直前にも、取消と実際のアンカーの適格性を再検査する。

submenuはアンカーの実surfaceから論理親popupを解決する。Passiveは入力focus、IME、pointer、wheel、menuの外側判定を奪わない。アンカーの実効非表示、retained panelの非活性化、接続世代の変更は既存managerの検査で閉鎖する。ControlsにはWindow型、OS stack、別のschedulerを持たせない。

### 公開APIの差分

基準コミットのControlsには以下の型は存在しない。抜粋のWidgetは`Lumyte.UI.Compose.Definitions.Widget`で、通常の生成factory、配置modifier、名前付き内容引数を維持する。

```diff
 namespace Lumyte.UI
 {
     public enum UiRole
     {
         Expander,
+        Menu,
+        MenuItem,
+        Tooltip,
     }
 }
 namespace Lumyte.UI.Controls
 {
+    // 受理値は各surfaceのElementだけに保持する。
+    public class MenuItemElement : UiElement<Compose.Definitions.MenuItem>
+    {
+        public bool IsChecked { get; }
+        public bool IsExpanded { get; }
+    }
+    public class TooltipElement : UiElement<Compose.Definitions.Tooltip>
+    {
+        public bool IsOpen { get; }
+    }
+    public interface IMenuPopupHost
+    {
+        // 借用host。要求はqueueし、内容factoryとclosedはowner loopで実行する。
+        IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, bool passive, Action closed);
+        // anchorを含むchainへItemInvokedをqueueする。
+        void ItemInvoked(UiElement anchor);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class Menu : Widget
+            {
+                public IMenuPopupHost? PopupHost { get; set; }
+            }
+            public partial class MenuItem : Widget
+            {
+                public Action? OnInvoke { get; set; }
+                public Func<Widget>? CreateSubmenu { get; set; }
+                public bool IsCheckable { get; set; }
+                public bool IsChecked { get; set; }
+                public Action<bool>? OnChanged { get; set; }
+                // 印のsRGB色。既定は不透明白で、有限な0〜1の各成分を検査する。
+                public SRgba<float> Foreground { get; set; }
+                // 正で有限。既定は240×160。
+                public Vector2 PopupSize { get; set; }
+            }
+            public partial class Tooltip : Widget
+            {
+                public IMenuPopupHost? PopupHost { get; set; }
+                public bool IsOpen { get; set; }
+                public Func<Widget>? CreatePopup { get; set; }
+                public Vector2 PopupSize { get; set; }
+            }
+        }
+    }
 }
+namespace Lumyte.UI.Controls.Windowing
+{
+    public sealed class MenuPopupHost : IMenuPopupHost
+    {
+        // managerとownerは借用。policyは既定HostedOnly。
+        public MenuPopupHost(UiPopupManager manager, UiPopupOwner owner,
+            UiWindowPolicy policy = UiWindowPolicy.HostedOnly);
+        // root構築時はownerを次のowner loopで解決する。
+        public MenuPopupHost(UiPopupManager manager, Func<UiPopupOwner> owner,
+            UiWindowPolicy policy = UiWindowPolicy.HostedOnly);
+    }
+}
```

Menu、MenuItem、Tooltipのroleをschema 9で追加する。新しいwire fieldは追加せず、schema 8の25 fieldを維持し、新roleだけを旧版ではnoneへ投影する。schema 1〜7の閉じた投影、数値情報、各操作で祖先全体から要求版の最大値を取る規則を維持する。診断操作は既存Click、Check、ExpandCollapseを通常入力として配送し、排他的leaseと生きた登録先のhit probeを省略しない。

## 検討した代替案

- WindowをControlsで作る方式は既存managerの清掃・入力scope・IME境界を重複するため採用しない。
- Menu表示用の別factoryは同じPopup引数と所有契約を重複させるため、既存Popupと内容の組み合わせを選ぶ。
- 自動hoverと時計をTooltip本体へ組み込む方式は入力triggerと表示状態の所有を混ぜるため、今回は明示状態へ限定する。

## 結果と影響

利用者は通常の宣言でメニューの意味情報、チェックとsubmenuを利用できる。Popup hostとモデルの表示状態、Tooltipを表示するtriggerはアプリが所有する。Dialog、Toast、組み合わせrecipe、ScopedSlotは対象外である。

検証は通常入力、popup admission、factory失敗、共有宣言、再入、取消、最新callback、retained anchorの非活性化、受動表示中の編集focusとpreedit保持、旧schemaのJSON、strict NativeAOTの実行を対象とする。Headless検証を物理host、WindowsまたはBrowserの実提示の確認として扱わない。
