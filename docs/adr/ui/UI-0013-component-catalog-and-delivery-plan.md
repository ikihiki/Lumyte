# ADR-UI-0013: UIコンポーネントの一覧・責務・優先度と実装分割

- 状態: 提案
- 日付: 2026-10-28
- 対応: コンポーネント拡充計画。既存の実装PR 1〜11を前提とする

## 背景

[UI-0001](UI-0001-composition-based-ui-system.md)〜[UI-0012](UI-0012-ime-and-text-editing.md)はツリー・layout・入力・描画・Window・Automation・IMEを定義したが、アプリが使う部品の一覧と追加順はまとまっていない。ゲーム内の設定画面と編集ツールを構築するには、基本表示、値入力、選択、スクロール、メニュー等の責務と依存を決める必要がある。

同じ状態機械を持つ部品を見た目ごとに別実装すると、keyboard/capture/focus/IME/Diagnosticsの対応が分岐する。一方、全てを巨大な汎用controlで表すと、Compositionによる組み立てと部品ごとの入力契約が曖昧になる。

## 決定

### 対象と優先度

46種類の表示・操作用Widgetを対象にする。非表示のPopup/Dialog portal宣言は接続基盤として別に扱い、この部品数へ含めない。既存ADRで提案済みのPanel、Stack、Rectangle、Button、TextBox、ImeCandidateViewの6種類を含み、残る40種類を拡充候補とする。基本8種類とSubmenuの計9種類の組み合わせ用factory、および非Widgetの共通基盤を別に扱う。

- P0: 設定画面・メニュー・簡単なフォームに必要な部品。既存PR 1〜11とC1〜C3で揃える。
- P1: 通常のアプリ画面・編集ツールで広く必要な部品。任意のImeCandidateViewを既存PR 11、残りをC4〜C8で追加する。
- P2: 大量データや複雑な編集ツールのための部品。C9で独立して扱う。

本ADRは部品一覧と共通境界の正本とする。C1〜C9は本計画内の機能群で、GitHub PR番号やADR番号ではなく、既存の実装PR 1〜11も再採番しない。各実装PRは詳細ADRを先に採番・レビューし、サイズ制約、操作、状態遷移、所有権、Automationの閉じたcommandと受け入れ条件を確定する。候補APIのstubは先行公開せず、詳細ADR・実装・検証を同じPRへまとめる。

### 配置とComposition

基本layoutと単色描画はLumyte.UIに置く。任意のLumyte.UI.MediaはGraphics.Text/画像描画へ、Lumyte.UI.Controlsは基本部品・Media・managedなTextEditingへ、Lumyte.UI.CollectionsはControls・scrollへ依存する。IMEは既存のLumyte.UI.TextEditing、Popup連携はLumyte.UI.Windowingへ置き、Popup連携を使う任意のLumyte.UI.Controls.WindowingがControls/CollectionsとUI.Windowingを参照する。Controls/CollectionsはWindow型を含まないPopup要求interfaceを借用し、具象Window操作をこのadapterへ委譲する。UI本体からPlatform、TSF、DOM、Resources、Diagnostics、テーマへ依存しない。

public型の基本形は各assemblyの`Compose.Definitions`に定義する。factory、with、子インデクサー、slotは[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)で生成する。以下のAPI候補の比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。既存6種類のAPIは各ADRを参照する。`Widget`はLumyte.UI.Compose.Definitions.Widgetの略記である。

新規Widgetは各Compose.Definitions内の[Composable] partial型とする。constructorと主要な状態・callbackの候補を示し、既定値や細部を確定する詳細ADRの担当をC番号で記載する。callback・値プロパティはどちらも`[ComposeParameter]`の対象とし、具体的なdelegate型はGeneratorが自動判別する。設定・置換は[UI-0005](UI-0005-pointer-dispatch-and-capture.md)のcallback契約に従う。

### 1. レイアウト・スクロール: 14種類

PanelとStackは[UI-0002](UI-0002-widget-tree-and-composition.md)・[UI-0003](UI-0003-layout-and-invalidation.md)のP0部品で、重ね合わせと一方向の並びを担当する。Row/Columnは別classにせず、StackのOrientationで表す。Spacerも専用状態を持たせずSizedBox/Expandedで表す。

```diff
+namespace Lumyte.UI
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P0/C1。追加制約を子へ渡す0〜1 child。子を配置後にresizeしない。
+            public partial class SizedBox : Widget { public SizedBox(); }
+            // P0/C1。追加min/maxを親制約内へclampする0〜1 child。要求自体のmin>maxは拒否。
+            public partial class ConstrainedBox : Widget { public ConstrainedBox(); }
+            // P0/C1。finite・非負の四辺余白を差し引いて子をlayoutし、余白を戻す。
+            public partial class Padding : Widget { public Padding(); }
+            // P0/C1。子へloose制約を渡し、返ったSizeの位置だけを決める。
+            public partial class Align : Widget { public Align(); }
+            // P1/C4。非flex子→残余領域のflex子の順で各子を一回layoutする。
+            public partial class Flex : Widget { public Flex(); }
+            // P1/C4。Flex直属の0〜1 childとfinite・正の重み。unbounded主軸では拒否。
+            public partial class Expanded : Widget { public Expanded(); }
+            // P1/C4。各childを一回layoutし、返った寸法で行を折り返す。
+            public partial class Wrap : Widget { public Wrap(); }
+            // P1/C4。固定/比率trackを先に解決して子へ制約を渡す。初期はintrinsic/Autoなし。
+            public partial class Grid : Widget { public Grid(); }
+            // P1/C4。Grid直属の0〜1 childとrow/column/span。範囲外・重複cellは拒否。
+            public partial class GridCell : Widget { public GridCell(); }
+            // P1/C5。viewportと内容offsetを持つ0〜1 child。初期は一軸、入れ子は明示仲裁。
+            public partial class ScrollView : Widget { public ScrollView(); }
+            // P1/C5。ScrollControllerを借用。thumb drag・track click・keyboardでoffsetを変更。
+            public partial class ScrollBar : Widget { public ScrollBar(); }
+            // P2/C9。複数paneの有限制約とdivider capture。min寸法・keyboard resizeを持つ。
+            public partial class SplitView : Widget { public SplitView(); }
+        }
+    }
+}
```

SizedBoxのWidth/Heightは既存Widgetの指定寸法を使い、空の場合も制約内の有限寸法を返す。ConstrainedBox/Padding/Alignは0〜1 child、Flex/Wrap/Grid/SplitViewは通常内容の複数childを所有する。ExpandedとGridCellを対応する親の外に接続したら検証で失敗する。ConstrainedBoxは親との不一致で空の制約を作らず、親制約を優先してmin/maxをclampする。

Flexは既存Stackの意味を変更しない。Gridは固定・比率・spanに絞り、Auto列は提供しない。比率trackには対応軸の有限maxを要求し、unboundedでは拒否する。親制約の優先と子サイズ確定後の配置は[UI-0003](UI-0003-layout-and-invalidation.md)に従う。

ScrollViewはscroll軸の内容へunbounded、交差軸へ有限制約を渡し、自身はviewportの有限サイズを返す。viewportを定めるscroll軸のmaxがunboundedなら、外側で有限寸法を与えるよう要求する。offset変更は描画/hit test/IME geometryへ同じ変換を適用する。ScrollBarとTextBox内部の水平表示offsetは別の状態で、二重scrollしない。wheel、touch pan、BringIntoView、入れ子の残余deltaとcapture仲裁をC5の必須契約にする。

### 2. 表示・装飾・進捗: 8種類

Rectangleは[UI-0004](UI-0004-drawing-and-clipping.md)のP0部品とする。Borderは見た目と内容を分けるdecoratorで、Borderの有無から子の入力可否を決めない。

```diff
+namespace Lumyte.UI
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P0/C1。0〜1 child。枠幅はfinite・非負で、内側の内容制約から差し引く。
+            public partial class Border : Widget { public Border(); }
+            // P0/C1。装飾線。既定でfocus/click capabilityを持たない。
+            public partial class Separator : Widget { public Separator(); }
+        }
+    }
+}
+namespace Lumyte.UI.Media
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P0/C2。ホスト供給の文字layout providerで描く。文字列を親幅で一度layoutする。
+            public partial class Text : Widget { public Text(); public string Value { get; set; } }
+            // P0/C2。ホスト供給の画像snapshotを描く。stretch/aspectとclipを明示する。
+            public partial class Image : Widget { public Image(); }
+            // P0/C2。sizeが明示されたvector/glyph/画像の意味付き表示。Button動作は持たない。
+            public partial class Icon : Widget { public Icon(); }
+            // P0/C2。finiteな0〜1の進捗表示。入力を受けずrole/stateで値を公開する。
+            public partial class ProgressBar : Widget { public ProgressBar(); public double Value { get; set; } }
+            // P0/C2。不定進捗の表示。clockを借用し、OS threadやtimerを自分で作らない。
+            public partial class BusyIndicator : Widget { public BusyIndicator(); }
+        }
+    }
+}
```

Textは一般の文字表示で、TextBoxの編集文書を作らない。選択可能Textは初期対象外とする。Image/IconはファイルpathやURLから同期loadせず、Resources等で準備した借用snapshotを受け取る。画像のintrinsic logical寸法と未供給時の占有寸法をC2で決め、ロード完了で制約なしにレイアウトを飛ばさない。GPU参照は既存の描画snapshotと[UI-0008](UI-0008-render-graph-and-resource-lifetime.md)の完了待ちへ接続する。

### 3. コマンド・値入力: 9種類

Buttonは[UI-0005](UI-0005-pointer-dispatch-and-capture.md)・[UI-0006](UI-0006-focus-and-keyboard.md)のP0部品とし、一回のactivationとOnClickを担当する。見た目が異なるだけのPrimaryButton/FlatButton等を別の状態機械にしない。

```diff
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P0/C3。押すごとにboolを切り替える。setterは通知せず、user操作だけがcallback。
+            public partial class ToggleButton : Widget
+            { public ToggleButton(); public bool IsChecked { get; set; } public Action<bool>? OnChanged { get; set; } }
+            // P0/C3。nullable boolで混在状態を表し、ThreeStateでuser操作の巡回を切替。
+            public partial class CheckBox : Widget
+            { public CheckBox(); public bool? IsChecked { get; set; } public bool ThreeState { get; set; } public Action<bool?>? OnChanged { get; set; } }
+            // P0/C3。RadioGroup内のordinalで一意なkey。単独で独立boolを保持しない。
+            public partial class RadioButton : Widget { public RadioButton(); public string Key { get; set; } }
+            // P0/C3。複数RadioButtonの単一選択と矢印移動を管理する。
+            public partial class RadioGroup : Widget
+            { public RadioGroup(); public string? SelectedKey { get; set; } public Action<string?>? OnChanged { get; set; } }
+            // P0/C3。on/off値。toggle behaviorを共有し、CheckBoxとは意味roleを分ける。
+            public partial class Switch : Widget
+            { public Switch(); public bool IsOn { get; set; } public Action<bool>? OnChanged { get; set; } }
+            // P1/C6。finiteなmin/max/step/value。drag中の変更とgesture確定を分ける。
+            public partial class Slider : Widget
+            { public Slider(); public double Value { get; set; } public Action<double>? OnChanged { get; set; } public Action<double>? OnCommitted { get; set; } }
+            // P1/C6。二つのthumbとlower<=upper。交差時にthumbの意味を暗黙交換しない。
+            public partial class RangeSlider : Widget { public RangeSlider(); }
+            // P1/C6。TextBoxの編集文書と最後に妥当だった数値を分離。locale/範囲を明示。
+            public partial class NumericBox : Widget
+            { public NumericBox(); public double? Value { get; set; } public Action<double?>? OnChanged { get; set; } }
+        }
+    }
+}
```

状態の正本はWidgetの値とする。user操作は妥当な新値を先にcommitし、値が変わった場合だけ最新のcallbackを一度取得して呼ぶ。アプリのsetter、items差し替え、最小値変更によるclampをuser callbackで通知しない。callback中のsetterを次のuser操作として再発火させない。TextBoxは既存のTextDocumentを正本とする例外であり、その契約を変更しない。

SliderはMin=Maxでも除算せず固定値として扱い、stepはfinite・正、値はmin/max内にclampする。cancelでは最後に成立した値を維持しOnCommittedを呼ばない。RangeSliderは一つのpointer/keyboard ownerが操作中のthumbを保持する。NumericBoxは入力途中の空/符号/不正形式を編集文書へ残し、妥当になるまでValueを変更しない。IMEのpreeditで数値を確定させない。

### 4. 文字編集・IME: 2種類

TextBoxは[UI-0012](UI-0012-ime-and-text-editing.md)のP0部品とする。単一行、選択、preedit/確定/cancel、Windows TSF、Browser textarea、Fillと確定値検証を担当する。一般のTextとは役割を分け、NumericBoxも同じ編集経路を再利用する。

ImeCandidateViewは[UI-0015](UI-0015-custom-ime-candidate-presentation.md)のP1部品で、既存PR 11に含める。候補一覧、選択、ページのsnapshotを描き、候補専用controllerを通して操作する。文字組みprovider、色・余白等のStyle、callbackはCompositionの名前付きパラメーターで供給する。editorのfocusを保持するCandidate入力modeで表示し、Menuや一般ListBoxの状態機械を流用しない。独自表示はTSFが候補情報を公開し、System表示を抑止できる場合に限る。BrowserではSystem表示を使う。C3/C5のcontrol・scroll基盤への依存は追加しない。

### 5. 項目・選択・階層: 8種類

```diff
+namespace Lumyte.UI.Collections
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P1/C7。key付きitemsをtemplateでWidget化する。選択/scrollを自分で持たない。
+            public partial class ItemsRepeater<T> : Widget where T : notnull { public ItemsRepeater(); }
+            // P1/C7。単一選択とkeyboard移動。scrollはScrollViewへ委譲し、初期は全件実体化。
+            public partial class ListBox<T> : Widget where T : notnull
+            { public ListBox(); public string? SelectedKey { get; set; } public Action<string?>? OnSelectionChanged { get; set; } }
+            // P1/C7。非編集の単一選択。閉じた表示とPopup内ListBoxで同じ選択keyを共有。
+            public partial class ComboBox<T> : Widget where T : notnull
+            { public ComboBox(); public string? SelectedKey { get; set; } public Action<string?>? OnSelectionChanged { get; set; } }
+            // P2/C9。展開key集合、階層keyboard、遅延children。循環/重複keyを拒否する。
+            public partial class TreeView<T> : Widget where T : notnull { public TreeView(); }
+            // P2/C9。行key/列定義、sort/resize/選択。初期はread-onlyでcell編集を分ける。
+            public partial class DataGrid<T> : Widget where T : notnull { public DataGrid(); }
+        }
+    }
+}
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P1/C8。TabItemの単一選択。focusと選択を区別し、inactive panelへ入力しない。
+            public partial class TabView : Widget
+            { public TabView(); public string? SelectedKey { get; set; } public Action<string?>? OnSelectionChanged { get; set; } }
+            // P1/C8。一意keyとHeader/Content。親TabViewだけがpanelの表示を決める。
+            public partial class TabItem : Widget { public TabItem(); public string Key { get; set; } }
+            // P1/C8。Headerと0〜1 Content。閉じる前に内容のIME/押下を取消しfocusを戻す。
+            public partial class Expander : Widget
+            { public Expander(); public bool IsExpanded { get; set; } public Action<bool>? OnChanged { get; set; } }
+        }
+    }
+}
```

ItemsRepeater/ListBox/ComboBoxは型付き`IReadOnlyList<T> Items`、`Func<T, string>? KeySelector`、`Func<T, Widget>? ItemTemplate`を構築パラメーターで受け取る。delegate型は自動判別してlambdaを直接渡せるようにし、必要なdelegateがnullなら実体化前に拒否する。TreeViewはroot itemsとchildren取得、DataGridはrow keyと列ごとのtemplateを追加する。itemsは受付時にコピーし、keyは非空のordinal識別子として重複を変更前に拒否する。templateは接続されていない新規rootを返し、失敗時に古い表示を保つ。既存Widgetを複数itemへ共有しない。

Itemsを再指定したら明示的に再構築し、keyで選択・展開・scroll anchorを復帰する。一般の自動reconciliationやTの変更監視を導入しない。作り直したWidgetのAutomation IDは新規とし、同じitem keyだからと古い接続IDを再利用しない。選択keyが消えたらnullへ戻す。初期ListBox/ComboBoxは単一選択とし、複数選択・範囲選択はC9の詳細契約で追加する。

TabItem/ExpanderのHeaderとContentはnamed slotとし、非選択Tabの内容は同じWidgetを保持して非表示・layout/入力対象外にする。表示切替で同じrootを別surfaceへ移さない。DataGridとTreeViewの仮想化はC9で実装し、C7で大量itemsに対応済みとはしない。選択不要のListView相当はItemsRepeaterとScrollViewで組み立て、同じ機能の別classを増やさない。

### 6. メニュー・補助表示: 5種類

```diff
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // P1/C8。MenuItemとSeparatorの並び。矢印/Enter/Escapeとsubmenu chainを管理する。
+            public partial class Menu : Widget { public Menu(); }
+            // P1/C8。activate、checked、submenuを識別。単純Buttonの見た目だけにしない。
+            public partial class MenuItem : Widget
+            { public MenuItem(); public Action? OnInvoke { get; set; } public Func<Widget>? CreateSubmenu { get; set; } }
+            // P1/C8。0〜1 anchor内容とpopup内容factory。Passiveで表示しfocusを移さない。
+            public partial class Tooltip : Widget { public Tooltip(); public Func<Widget>? CreatePopup { get; set; } }
+            // P1/C8。Header/Content/Actionsを組み立てる内容Widget。modal開始はhostが行う。
+            public partial class DialogContent : Widget { public DialogContent(); }
+            // P1/C8。一時通知の内容。表示時間やqueueは通知hostが所有する。
+            public partial class Toast : Widget { public Toast(); public Action? OnDismissRequested { get; set; } }
+        }
+    }
+}
```

Menuの表示はUI-0011のUiPopupManagerへ委譲し、ContextMenu/Dropdownは開き方として扱う。別のContextMenu WidgetやOS Window実装を追加しない。MenuItemがcommandならOnInvokeを一度呼び、命令的hostではchainのclose、宣言的MenuではItemInvokedのdismiss要求をqueueする。submenuならCreateSubmenuから新しいPopup内容を作る。初期はcommandとsubmenuの同時指定を拒否する。Separatorは既存の表示部品を使う。

Tooltip/Submenu/ComboBoxのPopup内容はfactoryで開くたびに新しいtreeを作る。接続は[UI-0002](UI-0002-widget-tree-and-composition.md)の所有規則、同一open内のfallbackは[UI-0011](UI-0011-popup-windowing-and-fallback.md)のfactory一回実行規則に従う。

DialogContentと表示portalのDialogを分ける。[UI-0022](UI-0022-declarative-hosted-dialogs.md)でページ内の宣言API、isOpen/isModal、閉鎖要求、同じ論理入力scope内の遮蔽、focus trap、IME終了、復帰と終了通知を定義する。C8のDialogはpresentationのAutomatic/Hosted/NativeRequiredで表示先を選び、Menu向けのPreserveFocusだけでNative modal対応済みとはしない。Nativeは所有/activation/owner入力抑止まで確認し、Browserや能力不足ではAutomaticを同canvas等のHostedへfallbackする。ページから独立した文書Window等はApplicationのWindow宣言を使う。Toastのtimer/queueはowner loopと借用clockを使い、任意threadからWidgetを変更しない。

DialogContentは46部品の既存Dialog候補からの改名であり、部品数は増やさない。`DialogCompose.Dialog`は非表示のportal宣言で、通常の利用者は`Dialog(isOpen: open, presentation: DialogPresentation.Automatic, onCloseRequested: ...)`をページ内へ置く。命令的Openサービスは初期範囲に含めず、詳細と未実装の検証条件はUI-0022を正本とする。

### 組み合わせ用factory: 基本8種類とSubmenu

次は独立した入力状態を持つWidget型にせず、通常のCompositionへ展開する。受け取った子の所有は[UI-0002](UI-0002-widget-tree-and-composition.md)に従う。配置は任意のLumyte.UI.Controlsの`UiRecipes`とする。

```diff
+namespace Lumyte.UI.Controls
+{
+    public static class UiRecipes
+    {
+        // Button + Icon。ButtonのOnClickとfocus/automationをそのまま使う。
+        public static Widget IconButton(Widget icon, string accessibleName, Action? onClick = null);
+        // Border + Padding。選択/activationを新設しない。
+        public static Widget Card(Widget content);
+        // labelとeditorの配置。accessible labelの関連を保持する。
+        public static Widget FormRow(Widget label, Widget editor);
+        // エラー文字の表示。検証器はアプリに置き、TextBoxを勝手に書き換えない。
+        public static Widget ValidationMessage(Widget? message);
+        // 既存Button/Separatorの並び。keyboard policyは詳細ADRで明示する。
+        public static Widget Toolbar(IReadOnlyList<Widget> actions);
+        public static Widget StatusBar(IReadOnlyList<Widget> items);
+        public static Widget EmptyState(Widget message, Widget? action = null);
+        // アプリが型に応じて選んだeditorを配置する。reflectionで型を推測しない。
+        public static Widget PropertyField(Widget label, Widget editor, Widget? validationMessage = null);
+    }
+}
```

上記8種類に加え、Controls.Windowing.MenuComposeのSubmenuを9種類目の組み合わせfactoryとして扱う。ContextMenu/Dropdown/hover triggerのrecipeは詳細ADRで採番・公開APIを確定する拡張候補であり、ここでは実装済みの追加factoryとして集計しない。

label/messageは呼び出し元がText等で作成して渡す。recipes内でフォントproviderやResourcesを暗黙取得せず、受け取ったWidgetと値だけを組み立てる。

### 共通基盤と入力・見た目の分離

UiSurface/UiBoxConstraints、pointer/keyboard/値gestureの認識器、focus scope、ScrollController、selection model、TextDocument、候補controller/presenter、IUiWindowManagerとNative/Hosted実装、UiPopupManager、Dialog/Toastのhost、文字/画像provider、style値、意味情報とAutomation hostはWidgetの種類数へ含めない。

UI専用アプリのbuilderと標準hostは[UI-0016](UI-0016-ui-application-framework.md)、navigationとページ寿命は[UI-0017](UI-0017-navigation-and-page-lifetime.md)、DIページ群のMapPage/MapGroupは[UI-0018](UI-0018-minimal-page-registration.md)へ置く。これらは非Widgetのフレームワークで、46種類の部品数へ加算しない。ページclassは非Composableとし、内部で既存のComposition部品を組み立てる。既存C1〜C9の前提や優先度を変更せず、UI専用アプリの導入は実装PR 12〜14として分ける。

C3でCompositeWidget相当の安全な単一内容root接続と共通activation/semantic target契約を追加する。内部のCompositionも既存ChildCollectionの検証へ通し、第二の所有ツリーを作らない。各部品がButtonのcapture/key状態をコピーせず、同じ認識器を使う。型階層を深くして見た目を継承させず、内容・背景・値・handlerの組み合わせで構成する。

surface input adapterは一登録なので、任意のcontrol入力とTextInputConnectionは一つのcoordinatorで組み合わせる。controlは[UI-0012](UI-0012-ime-and-text-editing.md)のeditorと同じ配送段階へ接続し、同ADRの優先順位を保って一度だけ配送する。slider/scroll等がcaptureした系列は取消と終端まで所有する。

styleは色・余白・線・文字設定と状態ごとの値を明示供給する。最初からglobal theme/binding/service locatorを必須にしない。描画だけの状態と値を分け、IsEnabled=false等で未完了入力をcallbackなしで取消す。Text/画像の描画とcontrolの操作状態を相互依存させない。

### 自動操作・意味情報

各操作部品でTestId、role/name、enabled、focus、checked/selected/expanded/valueの公開範囲を定義する。IconだけのButtonにもaccessibleNameを要求し、FormRow/PropertyFieldはlabelとeditorを関連付ける。ImeCandidateViewはUI-0015の秘匿規則に従い、通常snapshotのTestId/nameと子孫を公開せず、編集元pageと候補tokenで専用操作する。OSのアクセシビリティbridgeそのものは別途設計するが、意味情報を後で見た目から推測しない。

UI-0009の初期ClickはButtonだけなので、新roleを付けるだけで操作可能としない。C3以降は実際のsemantic target、hit probe、focus、内部templateの公開範囲を同時に設計する。Composite controlは意味上のownerを外部IDとし、内部装飾や認識用partを別のfocus候補として重複公開しない。

将来の操作はCheck/Uncheck、SelectOption、SetValue、Expand/Collapse、ScrollIntoViewを部品の導入PRで追加する。Diagnosticsの実行経路・plan・UInt64/座標は[UI-0010](UI-0010-diagnostics-ui-automation.md)、version 2への拡張は[UI-0012](UI-0012-ime-and-text-editing.md)、version 3の候補専用操作は[UI-0015](UI-0015-custom-ime-candidate-presentation.md)を引き継ぎ、新schemaの採番は各詳細ADRで決める。候補文字列は通常snapshotや子Widgetのnameへ公開せず、本文閲覧権限を必要とする専用取得で扱う。

仮想itemのLocatorはkeyを基準に探索・実体化待ちし、表示外のWidgetが存在するとは仮定しない。C9では仮想itemの意味情報、BringIntoView、再接続ID、surfaceを跨ぐPopupのpage選択、group操作権も受け入れ条件に含める。

### 実装PRの追加分割案

各機能群の納品範囲・完了条件・依存は次のとおりとする。

| 分割 | 優先度 | 納品範囲と完了条件 | 前提 |
| --- | --- | --- | --- |
| C1 | P0 | 基本decoratorと余白・整列・制約。単一childと一方向layoutの検証 | 既存PR 2・3 |
| C2 | P0 | Text/画像/Icon/進捗。幅依存文字組み、借用画像とGPU寿命 | C1、既存PR 7、GRAPHICS-0024 |
| C3 | P0 | 共通入力/semantic基盤、toggle・checkbox・radio・switch。callbackと通常入力の一致 | C1・C2、既存PR 5・8・11 |
| C4 | P1 | Flex/Expanded/Wrap/Grid/GridCell。有限制約と各子一回のlayout | C1 |
| C5 | P1 | scroll/scrollbar、wheel・pan・入れ子・BringIntoView・IME位置 | C1・C3、既存PR 11 |
| C6 | P1 | 数値・範囲入力、途中値/確定/取消・validation | C2・C3、既存PR 11 |
| C7 | P1 | keyed items、単一選択list、非編集combo。template失敗保証とPopup選択 | C2・C3・C5、既存PR 10 |
| C8 | P1 | Menu/tooltip、tab/expander、dialog/通知host、recipes。IMEとfocus scope・modalを検証 | C2・C3・C5、既存PR 10・11 |
| C9 | P2 | 仮想化、tree/read-only grid、pane分割。key/selection/scroll/Automationを維持 | C4・C5・C7 |

P0の受け入れ画面は「設定」: Text、余白と枠、Icon付きButton、CheckBox/Radio/Switch、TextBox、進捗をCompositionで構成し、Diagnosticsから通常操作と値検証を行う。P1は任意のIME候補独自表示とscroll付き一覧・combo・menu・数値入力を持つ画面、P2は大量項目の階層/表とpane分割を持つ編集画面で確認する。C4/C5/C6は条件が揃えば別PRとして並行レビューでき、一つの巨大な拡充PRへまとめない。

各機能群のPR分割は、単独で動作と受け入れ条件をレビューできる境界で決める。詳細ADR作成時はC8をmenu/tooltip、tab/expander、modal dialog/通知host、recipesへ、C9を仮想化基盤、tree/grid、pane分割へ分けることを検討する。

### 今回の対象に含めない部品

TextArea、PasswordBox、Date/TimePicker、ColorPicker、Autocomplete、RichTextEditor、Docking、Canvas/graph editor、WebView、OS file pickerは必要になった時点で独立した詳細ADRを設計する。TextAreaは複数行/scroll/selection、PasswordBoxは本文露出・IME・診断/clipboard、Dockingは別Windowと再配置の契約が必要であり、TextBoxやSplitViewの別名として追加しない。

### 宣言的なMenuと用途別Popup

2026-10-29追記。C8の利用者APIは[UI-0011の宣言的Popup](UI-0011-popup-windowing-and-fallback.md#宣言的なpopupの利用者api)を基盤に、表示状態・anchor・内容・callbackを静的factoryの引数へ渡す。通常利用でUiPopupManager.RequestOpen/AdvanceやWindow生成を記述させない。C8のMenuはメニュー内容の意味情報とkeyboard操作を担当し、Controls.Windowingのadapterが同名の表示factoryを提供する。実装assemblyを跨いだpartial Composeの共有は行わない。

利用者向けの追加factory候補は以下とする。key/anchor/isOpen/placement/presentation/onDismissは汎用Popupと同じ契約、値引数はSignal対応後に`UiArgument<T>`を受ける。MenuItemのOnInvokeは生成引数名もonInvokeとし、既存カタログの名称を維持する。

```csharp
// using static Lumyte.UI.Controls.Windowing.MenuCompose;
Menu(key: "file", anchor: "file-button", isOpen: menuOpen,
    onDismiss: _ => menuOpen.Value = false)[
    MenuItem(text: "保存", onInvoke: Save),
    Submenu(key: "export", text: "エクスポート")[
        MenuItem(text: "PNG", onInvoke: ExportPng),
        MenuItem(text: "SVG", onInvoke: ExportSvg)
    ]
]
```

| factory | 値・callbackの指定 | 内容と責務 |
| --- | --- | --- |
| Menu | Popup共通引数 | MenuItem/Separatorの一回初期化された宣言列。Menu入力modeとchainを管理 |
| MenuItem | text、isEnabled、isChecked、onInvoke | commandを一度実行し、rootへItemInvokedのdismiss要求を通知 |
| Submenu | key、text、isEnabled | MenuItemのsubmenu slotへ展開する組み合わせfactory。子の宣言列の接続をopenまで遅らせ、独立Widget型を追加しない |
| Tooltip | key、anchor、isOpen、onDismiss | Passive内容。開閉は引数が正本。hover/focus遅延は別のtrigger recipeへ分離 |

MenuItem command選択時の自動chain closeは、宣言的MenuではrootのonDismiss要求へ変換する。利用者モデルのisOpenを暗黙に変更しない。入力系列内のOnInvokeは一回とし、モデルがtrueのままならMenuは表示を継続する。submenuだけの開閉・選択・keyboard focusはMenu内部の表示状態として保持し、rootのisOpenを別の内部boolへコピーしない。Submenuの表示factoryは既存CreateSubmenuへ変換し、commandとの同時指定禁止を維持する。

ContextMenu/Dropdownは開閉triggerのrecipeとし、右クリックの位置/anchor、keyboard起動、dismiss callbackを同じMenuへ渡す。新しいOS Window型や別のPopup managerは作らない。Tooltip/ComboBoxも同じpresentation policyとportal寿命を使い、内容側の親子/slotを動的に書き換えない。MenuComposeの公開signature、trigger recipeの名前、文字layout providerはC8の詳細ADR・実装・テストで同時に確定する。表と例は未実装の設計であり、MenuItemのtext等を現在利用可能とは扱わない。

C8の検証には、command/チェック/submenu、Escape/外側pressの要求通知、応答しないモデル、連打/キーrepeatの一回実行、再構築でのsubmenu対応、focus復帰、Browser Hosted fallback、Popup単位のDiagnostics操作を含める。Submenuは既存MenuItemの組み合わせなので46 Widgetの集計は変更しない。

## 検討した代替案

- OSの標準control名を全て先に揃えると、実際に使う画面と入力・自動操作の完成が遅れる。P0/P1/P2と受け入れ画面で導入する。
- 見た目ごとにButtonの派生型を作ると、capture/focus/callbackの意味が分岐する。見た目だけのものはrecipesとする。
- ListBoxにscroll・仮想化・data binding・階層を全て内蔵すると単位が大きくなる。items実体化、選択、viewport、仮想化を別の責務へ分ける。
- catalogだけで全public APIを確定すると、未設計の入力や寿命を名前だけで約束することになる。主要な役割をここで固定し、詳細契約を導入PRで確定する。

## 結果と影響

実装すべき部品と組み合わせで済む部品、初期機能と大量データ向け機能を区別できる。新しい部品を同じComposition、layout、入力、IME、Window、Automationへ接続する責務も明示できる。

一方、C3の共通入力/semantic基盤、C5のscroll、C9の仮想化は各componentの見た目より先に設計する必要がある。

## 検証方針

- catalogの46部品Widget（Popup/Dialog宣言を除く）と9 recipes、P0=21/P1=22/P2=3、C1〜C9と既存PR 11の割当、前提の非循環を確認する。既存6種類を新規実装として重複計上しない。
- 各詳細ADRで親/slot/child数、入力系列、focus/cancel、状態変更とcallback、資源所有、意味roleとAutomation操作を対応付ける。
- 一方向layout、失敗後の旧tree維持、背景/clip、非一様DPI、Hosted座標、IME終端キー、Popup入力scopeを部品横断で検証する。
- pointer/keyboard/Diagnosticsの各操作で同じ値・callback回数を確認し、無効/非表示/取り外し/途中取消と古いIDを確認する。

### シグナル状態管理との関係

[SIGNALS-0001](../signals/SIGNALS-0001-independent-signal-runtime.md)の独立状態管理と[COMPOSITION-0003](../composition/COMPOSITION-0003-argument-adapters.md)の引数adapter、[UI-0019](UI-0019-signals-and-reactive-state.md)のUI自動購読を実装PR 15として追加する。通常のusing static構築からSignalを部品の値引数へ渡し、接続・動的な子変更・取り外し・終了に伴う購読をUIが管理する。値はSignal.Create/Computedで直接作り、利用者に構築scopeやruntimeを要求しない。非Widgetの状態管理なので部品数へ加算しない。C1〜C9の値入力部品もcallbackからモデルを更新し、IME文書編集の専用契約を維持する。

### アニメーションとの関係

[UI-0020](UI-0020-animation-and-frame-lifetime.md)を実装PR 16として追加する。Animateによる表示値は通常Composeの値引数へ渡し、UIが既存Animationの評価と接続寿命を管理する。これは非Widgetの表示値なので46 Widgetへ加算しない。寸法の変化は一方向layoutを毎frame進め、入力やIMEは提示済みgeometryを使う。部品側に独自timerやTweenを実装しない。

[UI-0021](UI-0021-connected-and-presence-transitions.md)は実装PR 17へ分離し、表示意図IsPresent、Transitions、SharedElementKeyとcallbackを既存Widgetの共通parameterとして追加する。新Widgetを増やさず46種類を維持する。初回表示/表示反転と通常値更新を区別し、明示の子除去は即時解除のままとする。page/共有要素はhostの視覚資源として保持し、部品側へ独自snapshotやnavigation寿命を実装しない。
