# ADR-UI-0034: タブと折り畳みで内容の寿命を保つ

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md)「配置とComposition」
- 変更対象: TabView・TabItem・Expanderの配置と構築契約
- 継続する判断: その他の部品区分、保持型所有権、明示的な見た目

## 背景

アプリの設定画面では、タブを替えたり詳細を閉じたりしても編集文書と購読を保つ必要がある。基準コードのRadioGroupは選択を持つが、内容の保持と入力除外を一体で扱うタブ部品はない。

## 決定

TabView、TabItem、Expanderを任意のLumyte.UI.Controlsへ追加する。内容は既存のElementツリーへ一度接続し、親が決める実効表示状態をlayout・描画・入力・意味情報へ共通適用する。

### 利用者の変更前・変更後

変更前はRadioGroupと条件付きのPanelを組み合わせる。非選択内容を構築結果から除くと、そのElementと編集状態は終了する。

```csharp
using static Lumyte.UI.Compose;
using Lumyte.UI.Controls;
using static Lumyte.UI.Controls.Compose;

var before = RadioGroup(selectedKey: "general")[RadioButton(key: "general")];
```

変更後は同じ内容をnamed slotへ渡す。以下の矩形は任意のTextやeditorへ置き換えられ、fontやthemeを暗黙取得しない。

```csharp
using static Lumyte.UI.Compose;
using Lumyte.UI.Controls;
using static Lumyte.UI.Controls.Compose;

var after = TabView(selectedKey: "general")[
    TabItem(key: "general")[
        TabItem.Header()[Rectangle(width: 120, height: 24)],
        TabItem.Content()[Expander(isExpanded: true)[
            Expander.Header()[Rectangle(width: 120, height: 24)],
            Expander.Content()[Rectangle(width: 120, height: 80)]
        ]]
    ]
];
```

### 所有権と選択

TabViewの通常内容は直属のTabItemだけを受け付ける。TabItemは既存Widget.Keyを使い、非空・ordinal一意とする。単独root、別の親、named slotへの配置を公開前に拒否する。HeaderとContentは各0〜1個のnamed slotであり、通常内容のインデクサーではない。

SelectedKeyが未知または削除済みならnullへ解決する。無効・非表示の選択項目は選択を保つが入力候補から外す。表示できる選択内容だけを配置する。並べ替えは既存の型とkeyによる照合を使い、別の親やsurfaceへ状態を移さない。投影検証の失敗では古い所有権・選択・入力を保つ。

選択とfocusは別状態である。通常のTab移動は有効な見出し一つへ入り、矢印とHome・Endは有効候補へfocusを移して自動選択する。明示的なfocus要求だけでは選択しない。見出し内の装飾は親の通常入力へ伝播し、Button等の対話部品は自身が受け付けた入力を所有する。親のkeyboard操作は親自身がfocusされたときだけ行う。

### 一回の配置

TabViewは左に縦の見出し列、右に選択内容を配置する。HeaderWidthは既定120、HeaderHeightは既定24で有限・非負とし、親の制約内へ収める。各Headerを割り当て済み寸法で一度、選択Contentを残り幅で一度配置する。非選択ContentへLayoutChildを呼ばず、既存の非表示処理へ任せる。親は子の確定寸法を後から伸縮しない。

### 内容の保持と取消

非選択内容と折り畳み内容はWidget・Element・編集文書・購読を保持する。宣言のIsVisibleを書き換えず、親の表示判定で子孫をlayout・描画・hit・focus・IMEから除外する。隠した部分木の幾何とlayout cacheは既存の非表示処理で失効させる。

選択変更で内容内のfocusを戻す場合は新しい選択見出しを優先し、無効なら有効な見出しを使う。閉じる状態を先に確定してから、子孫のpointerとkeyの所有を取り消し、focus終了を通知する。TextInputConnectionは通常のfocus終了でpreeditを終了する。focusが内容内にあった場合は見出しへの復帰を予約するが、通知中の新しいアプリfocus要求を上書きしない。取消や通知の例外でも残る清掃を試みる。

受理値はElementごとに保持する。同じ宣言の複数surfaceで共有せず、layoutや同じ宣言の子更新では保持し、新しい宣言の値は通知なしに採用する。ユーザー操作は値を確定してから最新callbackを一回呼ぶ。Signalへの書戻しはcallbackが明示する。pointer・Enter・Spaceは既存UiActivationRecognizerで統一し、取消では値を変えない。

### 公開API差分

基準コミットの実装へ次を追加する。UI-0013の未実装候補にあった可変TabItem.Keyは追加せず、init-onlyの継承値を維持する。

```diff
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // 通常内容は直属のTabItem。未知の選択はnull。
+            public partial class TabView : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public TabView();
+                public string? SelectedKey { get; set; }
+                public Action<string?>? OnSelectionChanged { get; set; }
+                // 有限・非負。既定120と24。
+                public float HeaderWidth { get; set; }
+                public float HeaderHeight { get; set; }
+            }
+            // HeaderとContentは生成named slotで各0〜1。
+            public partial class TabItem : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public TabItem();
+            }
+            // 初期値false。内容を閉じても接続を保持する。
+            public partial class Expander : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public Expander();
+                public bool IsExpanded { get; set; }
+                public Action<bool>? OnChanged { get; set; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI
+{
+    public partial class UiElement
+    {
+        // 保持された直属の子を通常表示へ参加させる。既定true。
+        protected virtual bool IsChildActive(UiElement child);
+        // headerだけを操作点とfocus可視化へ使い、内容の幾何を保つ。
+        protected virtual Lumyte.Graphics.Drawing2D.Rect2D AutomationActionRegion { get; }
+        protected virtual bool HitTestSelf(System.Numerics.Vector2 localPosition);
+    }
     public sealed class UiActivationRecognizer
     {
-        public UiInputResult HandleInput(in UiInputContext context, UiInputResult pointerDownDisposition, Action activate);
+        // nullなら従来のbody判定。headerは全系列へ自身の領域判定を渡す。
+        public UiInputResult HandleInput(in UiInputContext context,
+            UiInputResult pointerDownDisposition, Action activate, bool? pointerInside = null);
+    }
+}

```

各見出しは独立した内部Buttonを作らず、TabItemまたはExpander自身を意味上の操作対象とする。TabViewは既存SelectOption、TabItemはClickと選択情報を公開する。ExpanderはCheckBoxを偽装せず、通常Clickとkeyboardによる開閉、またはExpandAsync・CollapseAsyncを使う。後者はExpandCollapse能力と受理済みIsExpandedを検査し、既に目的値なら入力を送らない。変更時は排他的lease、再取得した幾何、実際のheader hit probeを検証し、UiExpandInputを一回配送する。

診断schema 8はtabview・tabitem・expander、expand・collapseとnullableなisExpandedを追加する。schema 1〜3の18 field、4〜5の23 field、6〜7の24 fieldは専用の閉じたserializerで保持し、新しいroleをnoneへ投影する。schema 8だけ25 fieldを公開し、数値metadataの意味は維持する。各型付きclient操作は祖先roleの要求版と操作の最低版の大きい方を使う。観測pageは入力を送れず、通常のObserveとOverrideInputの権限区分を維持する。

## 検討した代替案

- 非表示内容を破棄する方式は所有が簡単だが、編集状態を毎回アプリへ退避する責務が生じるため採用しない。
- Widget.IsVisibleを変更すると共有宣言の別surfaceへ影響するため、Element側の親判定を使う。
- Controlsから独立の所有ツリーを作る方式はlayout・入力・購読の取消経路が分裂するため、既存slotを使う。

## 結果と影響

内容の寿命を保ったまま入力対象を切り替えられる。利用者は表示内容と見出しを明示し、業務モデルへの書戻しをcallbackで行う。メニュー、通知、dialogと組み合わせfactoryは対象に含めない。

検証では共有宣言、投影検証失敗、並べ替えと削除、受理値の再入、入力取消、隠れた内容の文書同一性、実際の編集接続の終了、focus復帰と新しい要求の優先を確認する。物理OS IME、Windows、Browserの提示はheadless検証とは区別する。
