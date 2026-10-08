# ADR-COMPOSITION-0001: デリゲート型ファクトリとノード操作の生成

- 状態: 採用
- 日付: 2026-10-08

## 背景

設定値と子要素を C# の式でまとめ、`Grid()[Text(with: [Grid.Column(1)])]` と記述したい。旧 Lumyte.Composition の属性と Optional を基礎にするが、通常の静的メソッドは `Grid.Column` の拡張の receiver にできない。また、定義するクラスとファクトリのプロパティは同じ名前にしたい。

Composable クラス内の属性付き静的メソッドからファクトリへの拡張を生成する。生成されたノードを受け取って操作する共通の仕組みとし、添付値の保存領域は Widget 側が所有する。アニメーションの開始や tailwind 風のスタイル変更も同じ操作の生成で扱う。

## 決定

### 配置と責務

[ADR-0002](../0002-repository-layout.md) に従い、契約を `src/Core/Lumyte.Composition`、Generator を `src/Core/Lumyte.Composition.Generators` に配置する。契約のプロジェクト名・NuGet 名・名前空間は Lumyte.Composition とし、BCL のみに依存する。Graphics、UI、Engine、Native、Roslyn を実行時に参照しない。

契約は net10.0、Generator は netstandard2.0 と Microsoft.CodeAnalysis.CSharp 4.14.0 を使用する。Generator はビルド時の Analyzer とし、NuGet では `analyzers/dotnet/cs` に DLL を配置する。利用側は契約を通常参照し、Generator を Analyzer 参照する。NuGet の外部公開は行わない。

[ADR-0001](../0001-adr-writing-policy.md) に従い、本 ADR を初期実装の設計として採用する。DI、実行時探索、シリアライズ、差分更新、描画、子要素の所有権管理は扱わない。

### 定義と利用例

外側の public static partial クラスにファクトリのプロパティ群を生成し、内側の public static partial Definitions に同名の定義クラスを置く。「内部」は包含関係を表し、internal アクセス修飾子を意味しない。

```csharp
public static partial class Compose
{
    public static partial class Definitions
    {
        public abstract class Widget
        {
            public IDictionary<string, object?> AttachedValues { get; } =
                new Dictionary<string, object?>();
        }

        [Composable]
        public partial class Grid : Widget
        {
            [ComposeAction]
            private static void Column(Widget target, int value)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                target.AttachedValues["Grid.Column"] = value;
            }

            [ComposeContent]
            public IReadOnlyList<Widget> Children { get; set; } = [];
        }

        [Composable]
        public partial class Text : Widget
        {
            [ComposeParameter]
            public string? Content { get; init; } = "untitled";
        }
    }
}
```

上記には契約の名前空間と BCL の using が必要である。完全な実行可能な定義は [サンプル](../../../samples/Lumyte.Composition.Sample/README.md) に置く。

```csharp
using static Example.Compose;

var grid = Grid()[Text(with: [Grid.Column(1)])];
int column = (int)grid.Children[0].AttachedValues["Grid.Column"]!;
```

拡張メソッドの名前空間も import する。Definitions の型を外側と同時に static import して名前解決を曖昧にしない。Grid は利用側の例であり、契約ライブラリに UI 型を追加しない。

### 契約ライブラリの公開 API

比較元は Lumyte main の `36ced17bd093bb2c1bc360bd729f035c2655d9f7` とする。この revision に Composition API はなく、以下はすべて追加である。

```diff
+namespace Lumyte.Composition
+{
+    // 生成対象のクラスを明示する。
+    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
+    public sealed class ComposableAttribute : Attribute
+    {
+        public ComposableAttribute();
+
+        // 外側のファクトリクラス名。既定値 null はアセンブリ指定、それもなければ Compose。
+        // setter は値を保持し、識別子・衝突の検査は Generator が行う。
+        public string? Factory { get; set; }
+
+        // ファクトリプロパティ名。既定値 null は定義クラス名。別名指定は opt-in。
+        // setter は値を保持し、識別子・衝突の検査は Generator が行う。
+        public string? Name { get; set; }
+    }
+
+    // ファクトリ引数にする field/property を指定する。
+    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
+    public sealed class ComposeParameterAttribute : Attribute
+    {
+        public ComposeParameterAttribute();
+    }
+
+    // 子要素を置換する field/property を指定する。
+    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
+    public sealed class ComposeContentAttribute : Attribute
+    {
+        public ComposeContentAttribute();
+    }
+
+    // ノードを操作する静的メソッドを指定する。値の保存領域は利用側が持つ。
+    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
+    public sealed class ComposeActionAttribute : Attribute
+    {
+        public ComposeActionAttribute();
+    }
+
+    // アセンブリの既定ファクトリクラス名を指定する。
+    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
+    public sealed class CompositionDefaultsAttribute : Attribute
+    {
+        // factoryClass が null・空文字・空白なら ArgumentException。
+        // この検証は Generator による識別子検査の代わりにはならない。
+        public CompositionDefaultsAttribute(string factoryClass);
+
+        // コンストラクターで指定された既定クラス名を保持する。
+        public string FactoryClass { get; }
+    }
+
+    // 型制約なし。省略と明示的 default/null を区別する。汎用的な結果型へ拡張しない。
+    public readonly struct Optional<T>
+    {
+        // value が default/null でも指定済みとする。
+        public Optional(T value);
+
+        // default(Optional<T>) は false、コンストラクター・暗黙変換後は true。
+        public bool HasValue { get; }
+
+        // 未指定なら InvalidOperationException。
+        public T Value { get; }
+
+        // value を指定済みの Optional<T> へ変換する。
+        public static implicit operator Optional<T>(T value);
+    }
+}
```

### ジェネリックな定義

`Definitions.ListView<T>` も生成対象とする。C# に generic property がないため、外側には `ListView<T>(...)` メソッドと `ListViewFactory<T>()` デリゲート取得メソッドを生成する。非 generic 型のプロパティ方式は維持する。デリゲートは閉じた型引数の組ごとにキャッシュし、呼び出しごとに新しいノードを生成する。型引数は明示でき、通常の C# の型推論も利用できる。

```csharp
[Composable]
public partial class ListView<T> : Widget where T : notnull
{
    [ComposeParameter]
    public T Selected { get; set; } = default!;

    [ComposeContent]
    public IReadOnlyList<T> Items { get; set; } = [];

    [ComposeAction]
    private static void Select(ListView<T> target, T value) => target.Selected = value;
}

// using static Compose;
var list = ListView<int>(with: [ListViewFactory<int>().Select(2)])[1, 2, 3];
```

上の定義を Definitions 内に置いた場合の追加 API は次のとおり。

```diff
+public static partial class Compose
+{
+    // 型引数ごとにキャッシュした専用デリゲートを呼び出す。
+    public static Definitions.ListView<T> ListView<T>(
+        Optional<T> selected = default,
+        IReadOnlyList<Action<Definitions.ListView<T>>>? with = null) where T : notnull;
+    public static Definitions.ListViewFactory<T> ListViewFactory<T>() where T : notnull;
+
+    public static partial class Definitions
+    {
+        public delegate ListView<T> ListViewFactory<T>(
+            Optional<T> selected = default,
+            IReadOnlyList<Action<ListView<T>>>? with = null) where T : notnull;
+        public partial class ListView<T> where T : notnull
+        {
+            // 子要素を置換して同じインスタンスを返す。
+            public ListView<T> this[params T[] content] { get; }
+        }
+    }
+}
+public static class ComposeListViewCompositionExtensions
+{
+    public static Action<Compose.Definitions.ListView<T>> Select<T>(
+        this Compose.Definitions.ListViewFactory<T> factory, T value) where T : notnull;
+}
```

型パラメーター名と `class` / `class?` / `struct` / `unmanaged` / `notnull`、基底型・interface、`new()` の制約を、partial 型・デリゲート・ファクトリメソッド・拡張メソッドに引き継ぐ。複数型引数にも対応する。公開できない制約と `allows ref struct` は LYC001 で拒否する。後者は Optional と Action の契約に適合しない。操作メソッド自身の generic 化は引き続き対象外とする。

### 生成される公開 API

Grid/Text の例について、次の入口を生成する。以下はシグネチャの一覧である。

```csharp
// Compose.Definitions 内
public delegate Grid GridFactory(
    IReadOnlyList<Action<Grid>>? @with = null);
public delegate Text TextFactory(
    Optional<string?> content = default,
    IReadOnlyList<Action<Text>>? @with = null);

// Compose 内
public static Definitions.GridFactory Grid { get; }
public static Definitions.TextFactory Text { get; }

// Compose.Definitions.Grid 内
public Grid this[params Widget[] content] { get; }

// 名前空間直下の生成された静的クラス内
public static Action<Widget> Column(
    this Compose.Definitions.GridFactory factory, int value);
```

ファクトリごとに異なる名前付きデリゲートを使い、Func に共通化しない。Grid() はプロパティ値のデリゲート呼び出し、Grid.Column(1) はその値を receiver とする拡張メソッドになる。拡張は他のファクトリ型には適用できない。プロパティ取得だけでは構築せず、キャッシュした同じデリゲートを返す。デリゲート呼び出しは毎回新しいインスタンスを作る。

引数名・省略可能引数・既定値はデリゲート宣言に含める。接続先メソッドだけに既定値を付けても呼び出し時の省略には使えない。公開 setter や、公開入口となる通常の静的ファクトリメソッドは生成しない。

### 属性付き静的メソッドから操作を生成する

ComposeAction は Composable クラス自身の静的 void メソッドに付ける。先頭引数は操作対象の参照型、残りは捕捉する操作引数とする。例えば `private static void Column(Widget target, int value)` から `Action<Widget> Column(this GridFactory factory, int value)` を生成する。宣言メソッドは private でもよく、クラス内の生成 helper がアクセスし、名前空間直下の拡張からその helper を呼ぶ。

拡張の呼び出しは引数を捕捉した Action を返し、宣言メソッドを実行しない。with が構築したノードに Action を適用すると、その同じノードを先頭引数に渡してメソッド本体を呼ぶ。`Action<T>` の反変性により、`Action<Widget>` を派生 Text の with に渡せる。receiver は拡張の入口を識別するために使い、Grid のインスタンスを作らない。

追加引数がゼロの Reset、複数の引数を持つ Tag、添付値の設定やプロパティ書き換えなどを同じ規則で扱う。引数名と nullable を維持し、C# keyword を escape する。値の検査・保存・通知・アニメーション開始・レイアウト更新はメソッド本体の責務とし、Composition に保存 API、descriptor、Dictionary、弱参照 table を持たせない。Widget の具体的な保存形式も Core で規定しない。

Action は適用のたびに宣言メソッドを一度呼ぶ。再適用も別の副作用になり、冪等性は保証しない。参照型の捕捉引数はコピーされず同じ参照を使う。ユーザーコードの検証と例外は適用時に発生し、拡張の呼び出し時へ検証を先送り・先行実行することはしない。

初期対応は synchronous、非 generic、static void、先頭に参照型対象を持つメソッドとする。ref/out/in、params、optional 引数、同名の属性付き overload は LYC001 で拒否する。async void も完了と例外を with で観測できないため拒否する。生成拡張の公開シグネチャに出る型はすべて public を要求する。アニメーションなど非同期処理の完了を扱う契約は後続設計とする。

### 構築・with・子要素の順序

1. インスタンスの初期化子と引数なしコンストラクターを実行する。
2. required 引数と、指定済みの Optional 引数を適用する。未指定なら初期値を維持する。init メンバーの代入は構築中に行う。
3. with の null 要素を全件検査し、含まれていれば ArgumentException で拒否する。その後、アクションを列挙順に一度ずつ適用する。
4. 同じインスタンスを返す。続く子要素インデクサはその後に実行する。

with は最後の省略可能な引数とし、`IReadOnlyList<Action<TComponent>>? @with = null` を用いる。コレクション式を直接渡せるよう Optional で包まない。省略/null/空コレクションは追加設定なし。with は予約引数名であり、通常の生成引数との衝突を診断する。

アクションの例外は伝播し、後続を実行せず、インスタンスを返さない。すでに適用した値・コンストラクターやアクションの外部副作用はロールバックしない。書き込み可能な値は後のアクションが優先する。アクションは init を書き換えられず、required 引数の省略にも使えない。

子要素インデクサは配列を指定順で代入し、同じインスタンスを返す。繰り返し呼ぶと置換する。null 配列は ArgumentNullException。配列は複製せず参照を保持し、配列側からの変更も反映される。null 要素の可否は利用側の要素型とアプリケーションの責務に従い、Generator は要素の null 検査を行わない。破棄責任の移譲、ディープコピー、親子関係の検証は提供しない。

### 初期 Generator の対応範囲と診断

非 abstract な public partial クラス（generic を含む）を、public static partial 外側クラスの public static partial Definitions 内に置く形を扱う。包含型の階層を維持して partial 宣言を生成する。引数なしコンストラクターが必要で、暗黙定義の場合は生成コンストラクターの追加によって消えないよう明示定義を補う。

継承階層を走査し、アクセス可能な設定値と子要素を扱う。Inherited = false の ComposeContent でも基底型に宣言されたメンバーは走査する。子要素は高々一つで、一次元配列または IEnumerable/IReadOnlyCollection/IReadOnlyList/ICollection/IList の generic interface とする。init-only な子要素は拒否する。すべての required メンバーを ComposeParameter とし、生成コンストラクターが確実に代入してから SetsRequiredMembers を付ける。

引数順は required を先頭にし、各群では派生型から基底型、型内ではメンバー名の ordinal 順とする。引数名は先頭 underscore を除き先頭文字を小文字化し、C# keyword を escape する。任意の包含階層、アクセス不能な基底メンバー、override/隠蔽、非対応 collection、static/readonly 設定メンバー、複数 content、操作メソッドの不正な型や修飾子、生成名の衝突は LYC001 の error とする。生成メンバーの __Lumyte prefix は予約する。

### 互換性と環境

旧ライブラリの四属性と Optional の名前・適用先・省略の意味を保つ。ComposeAction 属性を追加し、旧 Generator の静的メソッドを専用デリゲートのプロパティへ変更する。定義型の階層とデリゲート型も公開契約になり、シグネチャ変更は拡張と利用側の再コンパイルを必要とする。旧生成物とのバイナリ互換性や全面的なソース互換性は保証せず、移行時は再生成する。

managed code と静的な生成コードを使い、実行時の反射・動的コード生成を必須にしない。Windows/Linux/Browser を将来の対象とするが、今回の実行検証は Linux の .NET 10 とする。Browser/AOT/trimming と Windows の動作は未検証である。

## 検討した代替案

### 通常の静的メソッドまたは Func 型

静的メソッドは拡張の receiver にできず、共通の Func 型では同じシグネチャの別ファクトリにも拡張が適用される。専用デリゲートと静的プロパティにする。

### 定義型とプロパティを同じクラス直下へ置く

C# の同名メンバーの制約に衝突する。定義型を Definitions、プロパティを外側へ分けて名前を保つ。

### descriptor フィールドから保存・getter・setter を生成する

宣言を集約できる一方、Composition が値の保存と寿命を所有することになる。Widget 側の保存領域と責務が重なるため採用しない。静的メソッドをラップすることで、添付値・スタイル・アニメーションなどの操作を共通化する。

### nullable/default を省略の印にする

null/0/false を明示できなくなる。Optional に指定の有無を独立して保持する。

## 結果と影響

- Composable 型内の属性付き静的メソッドから、対象型を保った操作の入口を生成できる。
- 初期値を維持しながら default/null を指定でき、保存形式に依存せず、利用側のノード操作を適用できる。
- デリゲートのキャッシュ、with のコレクション、引数を捕捉する Action の割り当てが発生する。
- インデクサの getter が対象を変更するため、置換と同一インスタンス返却を利用者へ説明する必要がある。
- 契約だけでは構築機能は動作せず、Generator と、その生成 API の互換性管理が必要になる。

## 検証方針

実際の Generator を Analyzer として参照したサンプルと、別アセンブリからのテストで要求構文をコンパイル・実行する。メソッドの遅延実行、同一対象への操作、Widget 側の保存、スタイル書き換え、ゼロ/複数引数、適用時の検証失敗、Optional、required/init、with の順序・例外、デリゲートのキャッシュ、新規 instance、子要素置換を確認する。

Roslyn のテスト compilation で、不正宣言の LYC001、required の省略、別ファクトリへの拡張拒否を確認する。NuGet をローカルに pack し、ProjectReference なしの consumer が Analyzer を発見・実行できることを確認する。build、dotnet format、Markdown lint と専用 CI で検証を継続する。

## 別途決定する事項

- Browser/AOT/trimming/Windows の環境検証。
- 任意の包含階層、override/隠蔽への対応。
- 操作メソッドの overload、optional/params 引数、非同期操作の完了・キャンセルの扱い。
- 性能測定と、NuGet 公開・バージョニング運用。

## 参考資料

- [旧契約](https://github.com/ikihiki/Lumyte_old/tree/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition)
- [旧 Generator](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Generators/CompositionGenerator.cs)
- [旧利用テスト](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Tests/CompositionTests.cs)
