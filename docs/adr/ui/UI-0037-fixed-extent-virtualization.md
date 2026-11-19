# ADR-UI-0037: 固定長の項目を表示領域だけ実体化する

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `a33bfadcce3d44dd6429bd179230d1d6a2381da6`
- 変更関係: 一部変更
- 変更元: [UI-0032](UI-0032-keyed-items-and-popup-selection.md#項目の準備と所有権)
- 変更対象: ListBoxの明示的な仮想化指定と項目の実体化範囲
- 継続する判断: 指定しない一覧の全件構築、キーによる選択、実体ごとの状態、項目の所有権

## 背景

大量の等間隔項目を表示する利用者は、画面外の内容まで構築する必要がない。基準コミットの `src/UI/Lumyte.UI.Collections/ItemsRepeaterElement.cs` は全項目のテンプレートを実行する。

## 決定

ListBoxへ固定長の仮想化を明示指定する。全件のデータとキーを検証して保持し、有限の表示領域と前後の予備項目だけを既存の所有ツリーへ接続する。

### 利用者の変更前・変更後

変更前は全件を構築する。

```csharp
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
string[] items = ["a", "b", "c"];
var before = ListBox<string>(items: items, keySelector: x => x,
    itemTemplate: _ => SizedBox(height: 24));
```

変更後は同じ一覧へ等間隔の指定を追加する。テンプレートの内容は割り当てられた行の制約で一度配置する。

```csharp
using Lumyte.UI.Collections;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
string[] items = ["a", "b", "c"];
var after = ListBox<string>(items: items, keySelector: x => x,
    itemTemplate: _ => SizedBox(height: 24),
    virtualization: new UiItemVirtualization(24, 2));
```

### 準備と配置

表示領域の幾何確定と項目の準備を別の段階にする。LayoutOverrideはツリーを変更しない。標準のUpdateLayoutは最大64段階の未公開の提案ツリー上で必要範囲を確定してから、一度だけ所有権と入力情報を公開する。途中で導入された入れ子の所有者も同じ境界で発見する。準備中の実体はmountせず、接続IDや接続資源を開始しない。[UI-0030](UI-0030-scroll-viewports-and-borrowed-controller.md#配置と状態の所有)の一つのScrollViewとcontrollerを使う。

項目長は有限かつ正、予備項目数は0以上の上限付き整数とする。件数との積をdoubleで検査し、floatの有限範囲を超える内容長を公開前に拒否する。0寸法の表示領域では、後述する入力保持枠を除いて項目を構築しない。縦横とも同じ計算を使う。

同じ採用済みモデルの範囲移動では重複部分の実体を保つ。除去後の再登場では新しい実体を構築し、接続IDをキーへ流用しない。Itemsの再指定では表示中の項目も新規構築する。全キーの検証はテンプレートより先に行い、準備失敗は旧ツリーを保つ。位置要求、内容長、表示長、anchor、実体の幾何と配置cacheは提案として保持し、全factoryの成功より前に受理値や利用側callbackを変更しない。複数の一覧も一括で検証し、一つの失敗で他の一覧だけを公開しない。公開後は全所有者の内容状態を採用してから利用側の更新処理を呼び、一つの例外でも別の所有者の採用を飛ばさない。項目の有効性と表示判定はモデルdelegateを用い、画面外のテンプレートから推測しない。

### 選択と入力

選択キーは全モデルへ照合し、画面外でも保持する。矢印とHome、End、Page移動はモデルの有効性を判定して対象範囲を予約し、次の準備境界でfocusを移す。実体化後の新しいfocus要求が優先する。スクロール時の重複項目は維持し、画面外のfocus項目は最大一件だけ追加保持して通常のclipへ置く。captureされた除去項目は既存の取消通知を経て切断する。通常の文字入力終了が非同期で待機中なら、同じ一件の保持枠を終了待ちのeditorへ優先する。モデル置換では全factoryを検証してから通常のpointer・key・focus終了を開始し、終了待ちの実体を切断する前にowner loopでsessionを排出する。待機中は旧モデルと受理した位置・幾何・購読を保つ。

キーの表示要求は実際のモデル所有者へ送り、roleだけでは操作できない。既存SelectOptionの通信形式は維持する。schema 10にitem-keyの検索段階とbring-item命令を追加し、キーとTestIdを区別する。入力lease、window hit、実際の選択所有者を検査してから通常入力で表示を要求し、選択では実体化後にpointerを使う。v1〜v9の検索とsnapshotのfield集合は変更しない。snapshotの件数は従来どおり接続中の実体の数であり、全データ件数へ読み替えない。GetByItemKeyのCountとassertionを含むObserve操作はスクロールや実体化を起こさない。キー表示要求はモデル世代と位置要求の順序を持ち、初期viewportとresize後の準備済み幾何で解決する。非同期要求もenqueue時に位置順序を予約し、後のwheel、明示ScrollTo、キー要求が先の要求に優先する。Clickと明示BringIntoViewには通常の入力権限を要求する。

### 公開API差分

```diff
+namespace Lumyte.UI.Collections
+{
+    // itemExtentは有限正、overscanは0〜1024。
+    public sealed record UiItemVirtualization
+    {
+        public UiItemVirtualization(float itemExtent, int overscan = 2);
+        public float ItemExtent { get; }
+        public int Overscan { get; }
+    }
+}
```

ListBoxの宣言に次を追加する。nullは従来動作を維持する。

```diff
 namespace Lumyte.UI.Collections
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
             public partial class ListBox<T>
             {
+                public UiItemVirtualization? Virtualization { get; set; }
             }
         }
     }
 }
```

表示実体へ次の操作を追加する。位置の予約だけを行い、内容準備はUpdateLayoutの境界へ委ねる。

```diff
+namespace Lumyte.UI
+{
+    public interface IUiItemViewport
+    {
+        bool BringIntoView(string key);
+    }
+    public sealed record UiBringItemInput(UiAutomationId Target, string Key,
+        System.Numerics.Vector2 Position) : UiInput;
+}
+namespace Lumyte.UI.Collections
+{
+    public class ListBoxElement<T> : IUiItemViewport
+    {
+        // 接続先thread上で操作。未知キーはfalse。
+        public bool BringIntoView(string key);
+        // 古いモデル世代は例外、dispatcherは借用。
+        public ValueTask BringIntoViewAsync(IUiDispatcher dispatcher, string key,
+            CancellationToken cancellationToken = default);
+    }
+}
```

位置の操作は既存のcontrollerを使う。宣言したScrollViewの即時採用は変えず、仮想一覧が所有するviewportだけ要求と受理値を分ける。

```diff
 namespace Lumyte.UI
 {
     public sealed class ScrollController
     {
+        // 仮想一覧では位置を予約し、全項目準備後に受理して最新callbackを一回取得する。
         public void ScrollTo(float offset);
     }
     public sealed partial class UiSurface
     {
+        // 仮想モデルは有限viewportと入力終了待ちを扱うUpdateLayoutで採用する。
         public void UpdateTree();
     }
     public partial class UiElement
     {
+        // 既定false。非同期入力sessionが現在の実体を借用している間だけtrue。
+        protected virtual bool PreserveRealization { get; }
     }
 }
 namespace Lumyte.UI.TextEditing
 {
     public interface IUiTextInputHandler
     {
+        // 既存実装の既定false。接続中または終了待ちのsessionだけを報告する。
+        bool HasPendingSession(TextBoxElement editor) => false;
     }
 }
```

検索の公開境界は次とする。キーは非空で最大256文字、検索段階は所有者の後へ一回だけ追加できる。文字の内容はordinal比較し、TestIdへ変換しない。

```diff
 namespace Lumyte.UI.Automation
 {
     public sealed partial class UiLocator
     {
+        public UiLocator GetByItemKey(string key);
+        // 通常の入力leaseを要求し、選択やfocusは変更しない。
+        public Task BringIntoViewAsync(UiWaitOptions? options = null,
+            CancellationToken cancellationToken = default);
     }
 }
 namespace Lumyte.Diagnostics.UI.Automation.Client
 {
     public sealed class UiRemoteLocator
     {
+        public UiRemoteLocator GetByItemKey(string key);
+        public Task BringIntoViewAsync(CancellationToken cancellationToken = default);
     }
 }
```

非表示の保持タブやExpanderでは既存の0範囲への補正を維持し、仮想項目の永続保持を約束しない。非表示でも未終了の入力sessionだけは上記の一件枠で終了を待つ。再表示は現在のキー範囲から新しい表示実体を構築する。

## 検討した代替案

- 可変長の自動計測: 項目の測定履歴と変更追跡が必要となるため、等間隔の初期基盤と分ける。
- 全件を構築して描画だけ省く: テンプレートと接続実体の数を減らせないため採用しない。
- 外部一覧framework: 既存の所有権と入力へ変換する層が必要となるため、既存のScrollViewとキー付き項目を使う。

## 結果と影響

利用者は等間隔を明示する責務を持ち、画面外の表示実体の保持に依存しない。件数ではなく表示領域と予備項目に応じた構築になる。可変高さ、階層、表形式は対象外とする。

検証では初期表示、範囲移動、再登場のID、空モデル、0寸法、サイズ変更、有限性と所有権の失敗を決定的な件数で確認する。実GPU、物理表示、OS IME、別RIDの実行結果とは区別する。
