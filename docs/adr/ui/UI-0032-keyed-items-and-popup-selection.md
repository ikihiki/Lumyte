# ADR-UI-0032: キー付き項目と非編集の選択表示を分離する

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `fd236f8f83f2c3e6d9566a1ce914f49b554209da`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md#5-項目選択階層-8種類)の項目部品と共通基盤
- 変更対象: 項目の実体化、選択値の正本、非編集の選択表示と表示先の境界
- 継続する判断: 型付き宣言、一つの所有ツリー、明示的な文字描画、通常入力と任意パッケージの依存方向

## 背景

一覧を作る利用者は、項目から表示を構築し、表示を差し替えても識別キーで選択を扱いたい。基準コードの `src/UI/Lumyte.UI/WidgetChildren.cs` は構築時の子を一回だけ受け入れるため、項目の再指定には表示実体側の準備と公開が必要になる。

[UI-0028の値の採用](UI-0028-selection-controls-and-shared-activation.md#値の採用と通知)、[UI-0030の表示領域](UI-0030-scroll-viewports-and-borrowed-controller.md#配置と状態の所有)、[UI-0011の入力範囲](UI-0011-popup-windowing-and-fallback.md#keyboard-scopeとpointer系列)を維持する。

## 決定

任意のCollectionsへItemsRepeater、ListBox、非編集ComboBoxを追加する。項目の準備と表示実体の受理値を分け、ポップアップ表示は既存の管理器へ借用インターフェースから委譲する。

### 利用者の変更前・変更後

変更前には専用の項目部品はなく、固定内容のStackを構築する。

```csharp
using static Lumyte.UI.Compose;
var before = Stack()[SizedBox(height: 24), SizedBox(height: 24)];
```

変更後は型、キー、内容の構築を指定する。文字を描く場合は、利用側が明示的なフォントと描画用providerをテンプレートへ渡す。

```csharp
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
var after = ListBox<string>(items: new[] { "a", "b" },
    keySelector: item => item, itemTemplate: item => SizedBox(height: 24),
    selectedKey: "a", onSelectionChanged: key => selected = key);
```

### 項目の準備と所有権

Itemsは採用の準備時にコピーする。必須delegate、非null項目、非空でordinal一意のキーを、最初のテンプレート実行より先に検査する。テンプレートは新しく、親と接続先を持たないrootを返す。項目間の宣言共有、循環、null、不正な親設定は公開前に拒否する。所有権の検査は宣言の子だけでなく、構築関数と表示実体の内容構築が準備した全子孫へ適用し、後続のSignalによる部分更新でも継続する。同じ項目内で同じ表示実体が保持する宣言は再利用できるが、他の表示実体へ接続済みの宣言は取り込めない。

準備失敗では以前の宣言、表示、選択、focus、位置、購読を保つ。準備中に接続資源や入力を開始しない。再指定では全項目の表示を作り直し、キーを接続IDへ流用しない。T自体の変更監視や仮想化は導入しない。

ItemsRepeaterは一方向のStackへ全項目を並べ、選択とスクロールを持たない。ListBoxは有限のScrollViewを使い、選択値と移動中の項目を別に保持する。項目の意味上の所有者は有限の交差軸を満たし、内容自体は一回の計測で返した寸法を維持する。無効・非表示の項目を移動候補から除く。矢印とHome／Endはfocusだけを動かし、Enter／Spaceまたはpointerの正常終端が選択する。PageUp／PageDownは五つの有効項目分移動する。テンプレート内の入力部品は独立した子優先入力先であり、その入力を項目選択へ偽装しない。

項目の再指定で同じキーのfocusを復元する場合は、準備時のfocus要求の世代を保持し、レイアウト後に接続中の置換項目へ適用する。その間に受理した明示focus、nullによる解除、bookmarkの復帰、移動要求があれば復元しない。同じ対象への要求も新しい意図として扱い、適格性を満たさない要求や古いbookmarkは復元を取り消さない。再構築に伴う内部のfocus解除は明示要求と区別する。

### 受理値とポップアップ

操作値はElementごとに保持し、宣言とモデルを書き換えない。新しい宣言は指定選択を静かに採用し、存在しないキーをnullへ補正する。操作は状態を先に受理してから最新callbackを一度取得する。例外でも受理値を戻さず、Signalへの書戻しは利用側の判断にする。

ComboBoxは独立した選択表示と空表示のfactoryを明示的に受け取る。編集文書とIME接続を作らず、開くたびに新しいListBoxを構築する。矢印による移動は未確定であり、項目の有効化で所有者へ選択を確定する。取消では通知しない。宣言の再採用や除去では古い要求を閉じ、古い世代からの選択を受け付けない。

CollectionsはUIとControlsへ依存し、Window型を公開しない。Controls.WindowingのCollectionPopupHostが既存UiPopupManagerと所有者を借用する。表示方式、開く前の文字入力終了、入力範囲、外側press、Escape、終了待ち、focus復帰は管理器へ委譲する。

### 意味情報と通信の互換性

ListBoxと各項目は実際の選択所有者とキーを既存の選択情報へ公開する。SelectOptionは通常pointer、実際のprobe、排他的入力leaseを通る既存操作を使う。ComboBoxは通常Clickとkeyboardで開き、popupは独立pageの同じ入力範囲を使う。roleだけで操作権を付与しない。

新しい三roleの検索は通信schema 7へ限定する。v1〜v3の18項目、v4〜v5の23項目、v6の数値情報を含む24項目は既存の形状を保ち、新roleだけnoneへ投影する。新しい数値fieldと値setter型commandを追加しない。Observe、OverrideInput、group lease、popupのpage分離を変更しない。

UI本体からCollectionsへの参照は追加しない。内部のfriendアクセスから準備結果へ項目の境界を不変の情報として渡し、公開前の提案ツリーと実体ごとの接続状態で所有権を検査する。Widgetへ接続先固有の所有情報を保存しない。スクロールの幾何確定時にキー付き位置を静かに復帰する。宣言の複数Surface接続そのものは禁止しない。

### 公開APIの差分

基準コードへ次の主要境界を追加する。生成器は基準コードの `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` の型引数付きfactoryを使う。

```diff
+namespace Lumyte.UI.Collections
+{
+    public interface ICollectionPopupHost
+    {
+        IDisposable Open(UiElement anchor, System.Numerics.Vector2 size,
+            Func<Lumyte.UI.Compose.Definitions.Widget> createContent, Action closed);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class ItemsRepeater<T> : Lumyte.UI.Compose.Definitions.Widget where T : notnull
+            {
+                public IReadOnlyList<T> Items { get; set; } // 採用時にコピー。
+                public Func<T, string>? KeySelector { get; set; } // 接続前に必須検査。
+                public Func<T, Lumyte.UI.Compose.Definitions.Widget>? ItemTemplate { get; set; }
+                public UiOrientation Orientation { get; set; } // 既定は縦。
+            }
+            public partial class ListBox<T> : ItemsRepeater<T> where T : notnull
+            {
+                public string? SelectedKey { get; set; }
+                public Action<string?>? OnSelectionChanged { get; set; }
+                public Func<T, bool>? ItemEnabled { get; set; }
+                public Func<T, bool>? ItemVisible { get; set; }
+            }
+            public partial class ComboBox<T> : ListBox<T> where T : notnull
+            {
+                public ICollectionPopupHost? PopupHost { get; set; }
+                public Func<T, Lumyte.UI.Compose.Definitions.Widget>? SelectedItemTemplate { get; set; }
+                public Func<Lumyte.UI.Compose.Definitions.Widget>? EmptyTemplate { get; set; }
+                public System.Numerics.Vector2 PopupSize { get; set; } // 有限かつ正。
+            }
+        }
+    }
+}
```

## 検討した代替案

- 汎用スロット拡張を先に作る: 関係のない部品の契約まで広がるため、既存の型付き項目factoryと実体所有の内容構築を使う。
- Widgetへ選択を保存する: 宣言の複数接続で値が混在するため、表示実体に保存する。
- ポップアップを同じ子ツリーへ接続する: clipと入力範囲が一致しないため、既存の独立Surfaceを使う。
- 外部一覧frameworkを取り込む: 既存の宣言、所有権、入力、描画へ変換する層が必要になるため、既存Stack、ScrollView、認識器を組み合わせる。

## 結果と影響

利用者は型付き項目から繰返し表示と単一選択を構築できる。文字描画とポップアップの借用寿命を利用側で明示する責務が増える。全件を実体化するため大量項目の仮想化、階層、複数選択は対象外である。

検証では失敗後の旧ツリー、共有宣言の独立、最新callbackと例外、削除とキー変更、通常pointerとkeyboard、位置移動、ポップアップ取消とfocus復帰を確認する。Headlessの検証と実GPU、OS文字入力、他の実行環境の保証を区別する。
