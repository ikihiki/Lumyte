# ADR-UI-0040: 型付きの列表示を持つ読み取り専用表を提供する

- 状態: 採用
- 日付: 2026-11-20
- 基準コミット: `4c8a316879d809fc47025a769524c5e4684a50c4`

## 背景

表を表示する利用者は、行選択を保ちながら複数の名前付き列を並べたい。基準コミットの `src/UI/Lumyte.UI.Collections/Compose.ListBox.cs` は行選択と固定長仮想化を提供するが、列設定と表のヘッダーを持たない。

## 決定

DataGrid を Collections に追加し、列の設定を不変値として受け取る。ヘッダーとセルの表示には既存の型付き CompositionTemplate と名前付き Slot を使い、別のテンプレート基盤を作らない。

### 利用者の変更前・変更後

変更前は ListBox の Item に行全体の表示を渡し、列幅とヘッダーを利用側で管理する。以下では rows、keySelector、provider の準備を省略する。

```csharp
using Lumyte.UI.Collections;
using Collections = Lumyte.UI.Collections.Compose;
using Media = Lumyte.UI.Media.Compose;

var list = Collections.ListBox<Person>(items: rows, keySelector: person => person.Id)[
    Collections.ListBoxFactory<Person>().Item()[
        (person, owner) => Media.Text(value: person.Name, layoutProvider: provider)]];
```

変更後は列設定と Header、Cell の Slot を渡す。テンプレートの第二引数は評価時の具体的な DataGrid 宣言であり、表示実体の受理状態ではない。

```csharp
var table = Collections.DataGrid<Person>(
    items: rows, keySelector: person => person.Id,
    columns: new[] { new DataGridColumn<Person>("name", "名前") })[
    Collections.DataGridFactory<Person>().Header()[
        (column, owner) => Media.Text(value: column.Name, layoutProvider: provider)],
    Collections.DataGridFactory<Person>().Cell()[
        (cell, owner) => Media.Text(value: cell.Row.Name, layoutProvider: provider)]];
```

### 所有権と準備

行と列の配列を利用者の関数より先にコピーする。行と列のキーは非空の ordinal 識別子とし、それぞれ重複を拒否する。列幅は有限の正値で、最小値と最大値を満たす。比較は列の明示的な IComparer に限定し、等しい行は元の行番号の順序を保つ。元の Items を並べ替えない。

Header と Cell は評価先ごとに新しい未接続の構成を返す。[UI-0038 の評価と所有権](UI-0038-typed-template-slots.md#評価所有権再構築)を継続し、固定の構成を直接渡す構文も複製や共有の許可を意味しない。再構築では既存の RegisterTemplateSlot を使い、再構築先の宣言を渡す。内部の表示には既存の UiFunctionWidget を使い、テンプレート本体の Signal 読み取りを表示実体ごとに追跡する。生成済みのヘッダーとセルだけを再評価し、未実体化の行に表示や購読を作らない。

ヘッダーは垂直スクロールの外側に置く。横方向にはヘッダーと行を同じ一軸の ScrollView へ入れ、列幅と横位置を一致させる。垂直方向は ListBox の固定長仮想化を再利用する。可視行の全列を実体化し、横方向の列仮想化は提供しない。

### 受理状態と通常入力

選択、列幅、並べ替えは表示実体ごとに保持し、宣言や呼び出し側の配列へ書き戻さない。宣言の値が変わった場合は通知せず採用する。同じ宣言値を使う引数シグナルの再構築では、操作による受理値を維持する。削除された行の選択は解除し、非表示または削除された列の並べ替えは元の順序へ戻す。活動列が消えた場合は最初の可視列へ移る。

ヘッダーの本文クリックと Enter／Space は、明示比較を持つ列について昇順、降順、元の順序を循環する。同じヘッダーの右端8論理単位は幅変更の入力領域とし、本文の自動操作位置から除外する。幅変更はポインター捕捉または左右キーの8単位移動を使い、最小値と最大値へ制限する。取消しでは既に受理した幅を維持し、並べ替えや別の完了通知を発生させない。

セルは上下で同じ列の行間、左右で同じ行の列間を移動する。Home／End は行の先頭と末尾、PageUp／PageDown は可視行数に基づく移動とする。これらは活動位置とフォーカスだけを変え、選択は本文クリックと Enter／Space で行う。修飾キー付きの独自操作は定義しない。

ヘッダーのテンプレートは装飾として扱い、独立した入力先にしない。セル内の通常の Button や TextBox は子優先の独立入力先とし、その操作で行を二重選択しない。DataGrid 自身は編集開始、編集確定、表への書戻しを持たず、GridCell に Fill の能力を公開しない。利用者が明示した編集部品の通常入力とは区別する。

モデル、列幅、並べ替え、可視範囲とテンプレートを同じ Surface の公開前準備へまとめる。比較、キー選択、内容生成が失敗した場合は古い受理値、表示実体と位置を維持する。公開前の関数から同じ Surface の並べ替え、幅変更、表示要求へ再入することは、位置予約を作る前に拒否する。成功した受理状態は利用者への通知より先に採用し、通知関数の例外で巻き戻さない。親が内容の置き換えを提案した反復では、その旧子ツリーの追加準備を行わず、次の投影済みツリーで子を準備する。これにより、新しい列幅や行モデルの提案へ旧子の factory と内容を重ねない。

行と列の上限はそれぞれ100万件と1024列、キーと明示名は256文字とする。固定長の積と列幅の合計は有限の float で表現できる必要がある。利用者の行オブジェクト自体を複製せず、受付時の値とキーの配列を保持する。全列を各可視行へ実体化するため、列数の上限は水平仮想化の代替ではない。

### 観測と表示要求

DataGrid、GridRow、GridCell は実際に接続された要素の役割とする。セルの親は実際の行であり、セルの不変メタデータには行キー、列キー、投影後の行番号と列番号を公開する。未実体化の行やセルへ接続IDを合成しない。ヘッダーの名前は列設定の明示名を使い、テンプレートの文字から推定しない。

GetByItemKey の下の GetByColumnKey は実体化済みセルだけを観測する。Count と assertion は範囲を作らず、比較やテンプレートを呼ばない。明示的な表示操作は排他的な通常入力 lease を取得し、実際のウィンドウと modal・menu の入力範囲を検査してから行と列の表示を予約する。垂直位置と水平位置の変更も公開前の同じ準備へ含める。GridCell の FocusAsync は、安定した自身の入力領域を通常のポインター Down／Cancel で操作し、行を選択せずフォーカスだけを得る。子の入力領域をセル自身の領域と偽らず、子部品を操作する場合はその子を明示的に指定する。

行の非同期表示要求は既存の ListBox のモデル世代と位置予約を再利用し、新しい要求や宣言の採用後に古い要求を実行しない。範囲外のフォーカスまたは進行中入力の保持は C9 の最大一行の枠を再利用する。項目キーの一致だけで別の一覧の行を保持せず、実際の所有実体も一致する場合に限定する。内容シグナルで表示を再構成した場合は、行と列のキーでセルのフォーカスを復帰し、行の受動的なラッパーへ移さない。仮想化による保持を全行の常時接続へ拡張しない。

診断 schema 12 に役割とセル情報、列キー query を追加する。既存 schema 1〜11 は元のフィールド形状を専用のシリアル化型で保持し、新しい役割を混入させない。型付きクライアントは全祖先 query と操作自身の最低版の最大値を使い、古い catalog へ入力を送る前に拒否する。

### 公開 API と契約の差分

```diff
 namespace Lumyte.UI.Collections
 {
+    // 非空のキーと名前、有限の幅範囲、可視性、明示的な比較を持つ不変値。
+    public sealed record DataGridColumn<T> where T : notnull
+    {
+        public DataGridColumn(string key, string name, float width = 120,
+            float minimumWidth = 24, float maximumWidth = 4096,
+            IComparer<T>? comparer = null, bool isVisible = true);
+        public string Key { get; }
+        public string Name { get; }
+        public float Width { get; }
+        public float MinimumWidth { get; }
+        public float MaximumWidth { get; }
+        public IComparer<T>? Comparer { get; }
+        public bool IsVisible { get; }
+    }
+    public sealed record DataGridCellContext<T>(T Row, string RowKey,
+        int RowIndex, DataGridColumn<T> Column, int ColumnIndex) where T : notnull;
+    public enum DataGridSortDirection { None, Ascending, Descending }
+    public sealed record DataGridSort(string? ColumnKey, DataGridSortDirection Direction);
+    public class DataGridElement<T> : UiElement<Compose.Definitions.DataGrid<T>>, IUiGridViewport
+        where T : notnull
+    {
+        // 読み取り専用の受理値。要求の採用は次のレイアウト境界で行う。
+        public IReadOnlyList<string> Keys { get; }
+        public IReadOnlyList<float> ColumnWidths { get; }
+        public DataGridSort Sort { get; }
+        public string? SelectedKey { get; }
+        public bool CycleSort(string columnKey);
+        public bool ResizeColumn(string columnKey, float width);
+        public bool BringIntoView(string key);
+        public bool BringCellIntoView(string rowKey, string columnKey);
+        public ValueTask BringIntoViewAsync(IUiDispatcher dispatcher, string key,
+            CancellationToken cancellationToken = default);
+    }
 }
```

DataGrid は Items、KeySelector、Columns、SelectedKey、Sort を構築引数として受け取る。Header と Cell の Slot はそれぞれ列設定またはセル文脈と具体的な DataGrid 宣言を受け取る。セル編集エンジンは提供しない。

DataGrid と列設定は Lumyte.UI.Collections、役割と不変のセル座標は Lumyte.UI に置く。下記は生成する Slot 入口の抜粋で、Widget は Lumyte.UI の宣言型、CompositionTemplateSlot は既存の Lumyte.Composition の型である。

```diff
 namespace Lumyte.UI.Collections
 {
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class DataGrid<T> : Widget where T : notnull
+            {
+                // 初期 Height=240、HeaderHeight=28、行長28、overscan=2。
+                public DataGrid();
+                public IReadOnlyList<T> Items { get; set; }
+                public Func<T, string>? KeySelector { get; set; }
+                public IReadOnlyList<DataGridColumn<T>> Columns { get; set; }
+                public UiItemVirtualization Virtualization { get; set; }
+                public float HeaderHeight { get; set; }
+                public string? SelectedKey { get; set; }
+                public DataGridSort Sort { get; set; }
+                public Action<string?>? OnSelectionChanged { get; set; }
+                public Action<DataGridSort>? OnSortChanged { get; set; }
+                public Action<string, float>? OnColumnWidthChanged { get; set; }
+            }
+        }
+    }
+    public static class ComposeDataGridCompositionExtensions
+    {
+        public static CompositionTemplateSlot<Compose.Definitions.DataGrid<T>,
+            DataGridColumn<T>, Compose.Definitions.DataGrid<T>, Widget> Header<T>(
+            this Compose.Definitions.DataGridFactory<T> factory) where T : notnull;
+        public static CompositionTemplateSlot<Compose.Definitions.DataGrid<T>,
+            DataGridCellContext<T>, Compose.Definitions.DataGrid<T>, Widget> Cell<T>(
+            this Compose.Definitions.DataGridFactory<T> factory) where T : notnull;
+    }
 }
 namespace Lumyte.UI
 {
+    public sealed record UiGridCellState(string RowKey, string ColumnKey,
+        int RowIndex, int ColumnIndex);
+    public interface IUiGridViewport : IUiItemViewport
+    {
+        bool BringCellIntoView(string rowKey, string columnKey);
+    }
+    // UiRole に DataGrid、GridRow、GridCell を末尾追加する。
 }
 namespace Lumyte.UI.Automation
 {
     public sealed partial class UiLocator
     {
+        public UiLocator GetByColumnKey(string key);
     }
 }
```

Slot は未評価の delegate を受け取り、UI 側が列またはセル文脈と評価先の具体的な宣言を渡す。列の Width は宣言設定であり、実体の受理幅を宣言へ書き戻さない。ヘッダーやセルの構築関数が現在の受理状態を必要とする場合は、宣言の owner から暗黙に取得できるとは扱わない。

## 検討した代替案

- ListBox の利用例だけを追加する: 行の仮想化を再利用できるが、列幅とヘッダーの整合を各利用者が実装する必要がある。表として一つの所有者へまとめる。
- 列を Widget とする: 生成された Slot の対象にできるが、列設定と表示実体の所有権を混同する。列は不変の設定値にし、具体的な DataGrid の Slot を使う。
- 文字列から比較を推定する: 呼び出しは短いが、文化設定と表示テンプレートに順序が依存する。明示的な比較を採用する。

## 結果と影響

利用者は列の表示と比較を型付きで定義できる。借用する文字組みサービスは呼び出し側が明示的に供給する。比較とテンプレートの例外、重複キー、幅の不正値では公開前の表示を維持することを検証する。生成 Slot のラムダ推論、再構築先の owner、共有構成の拒否、可視行に限定した生成、選択、並べ替えと幅変更の通常入力を受け入れ条件とする。
