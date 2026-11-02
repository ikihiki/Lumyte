# ADR-COMPOSITION-0005: 構築関数の実行時期を利用側で選べる宣言を生成する

- 状態: 採用
- 日付: 2026-10-30
- 基準コミット: `fbc81051cbf0f4aa5d996237543765e02b8cfa5c`（既存判断の説明を整理する基準）

## 背景

部品を作る利用者は、静的関数の引数と戻り値で構造を表現しながら、評価の時期や部品の識別を管理したい。関数を直接呼ぶだけでは、その場で本体が実行されるため、状態を照合してから評価する境界が得られない。

## 決定

`[Composable]` を付けた静的関数から、関数をまだ実行しない宣言を作るメソッドを生成する。宣言は利用側が用意する基底クラスを継承し、利用側が `Build` を呼んだ時点で元の関数を実行する。評価・状態管理を引き受けるこの利用側を、以下ではホストと呼ぶ。

### 利用者の変更前・変更後

文字列の部品を作る同じ操作を、直接呼び出す場合と生成メソッドを使う場合で示す。使用する型と関数は、基準コミットの `samples/Lumyte.Composition.Sample/Compose.cs` にあり、両方とも基準時点で利用できる。

変更前:

```csharp
using Lumyte.Composition.Sample;

int calls = 0;
var text = (Compose.Definitions.Text)Compose.Definitions.Greeting(
    onBuild: () => calls++);
// calls == 1、text.Content == "hello"。その場で関数を実行する。
```

変更後:

```csharp
using Lumyte.Composition.Sample;

int calls = 0;
var node = (Compose.Definitions.FunctionWidget)Compose.Greeting(
    onBuild: () => calls++, key: "greeting");
// calls == 0、node.Key == "greeting"。ここでは宣言だけを作る。

var text = (Compose.Definitions.Text)node.Evaluate();
// calls == 1、text.Content == "hello"。ホストが選んだ時点で実行する。
```

`FunctionWidget` と `Evaluate` は例のホストが提供する型とメソッドである。`Evaluate` は生成された `Build` を呼ぶ。この例に評価結果のキャッシュはなく、再度呼べば関数本体も再実行する。`onBuild` は元の `Greeting` が明示的に呼ぶコールバックである。

### 定義とホストの契約

外側の `Compose` と内側の `Definitions` を公開された静的な部分クラスとし、定義関数を `Definitions` に置く。外側のクラスと生成メソッドの名前は `Composable.Factory`・`Name`・`CompositionDefaults` の既存規則で選ぶ。クラスによる定義も併用できる。

関数の戻り値の型、またはその基底型へ `ComposeFunctionAdapter` を付け、宣言の基底クラスを指定する。このクラスは次を満たす。

- 公開された非ジェネリックの抽象クラスで、公開または保護された引数なしコンストラクターを持つ。
- 引数・型引数を持たない `protected abstract Build` を宣言する。元の関数の戻り値と基底クラス自身を、その戻り値の型へ暗黙変換できる。
- `string` 型の `Key` に公開 `set` または `init` を持つ。生成時の `null` を受け取れるよう、null 許容または注釈なしで宣言する。
- 生成クラスに `Build` 以外の抽象メンバーの実装を要求しない。

生成メソッドはオブジェクト初期化子で `Key` を設定するため、初期化後に変更できないプロパティも受け入れる。ホストはキーの照合、状態の寿命、評価時期を管理する。`Build` の返り値の検証、例外、副作用の扱いもホストの責務である。

関数ごとに非公開の専用クラスを生成し、ホストの基底クラスを継承させる。その `Build` が元の定義関数を型付きで呼ぶ。生成メソッドの戻り値は `Build` の戻り値型とし、専用クラスの型を公開しない。

### 引数と再構築

引数の名前・順序・型・既定値・型制約を引き継ぎ、末尾に `string? key = null` を加える。具体的なデリゲート型はそのまま受け取る。既定値は生成先から解決できる型付き定数として出力し、decimal・浮動小数・null 許容の列挙型・特殊値も扱う。

値引数の変換を指定する場合は、[ADR-COMPOSITION-0003 の引数の生成規則](COMPOSITION-0003-argument-adapters.md#引数の生成規則)を使う。関数の戻り値側から、完全一致・ジェネリック型定義・共通指定の順で変換型を選び、宣言型に一致する `IComposeArgument<T>` と型制約を検査する。

変換型を使う非デリゲート引数は、`GetInitialValue` で値を取得して宣言へ保存し、`AttachTo` で受動的な情報を登録する。省略可能な引数の未指定値では両方を呼ばず、定義関数の既定値を使う。指定された値は明示した既定値も含めて登録する。具体値の `null`・`default` は指定値として扱い、必要なら変換型の `Constant` などで表す。必須引数の未指定は拒否する。

ホストが `ICompositionRebuildable` を実装する場合、元の引数とキーで新しい宣言を作る関数を登録する。この関数は呼ばれるまで再構築せず、呼ばれても定義関数の本体は実行しない。値引数の変換を使う場合は、再構築時に値を解決し直す。

```csharp
var copy = (Compose.Definitions.FunctionWidget)node.Factory!();
// copy は別の宣言、copy.Key == "greeting"、calls は1のまま。
var copiedText = (Compose.Definitions.Text)copy.Evaluate();
// copiedText.Content == "hello"、calls == 2。

var cleared = (Compose.Definitions.FunctionWidget)Compose.Greeting(label: null);
var clearedText = (Compose.Definitions.Text)cleared.Evaluate();
// clearedText.Content == null。省略時の "hello" と区別する。
```

再構築は元のコールバックも引き継ぐが、部品の状態や子の所有関係を自動移行しない。関数が別の生成メソッドを呼んで宣言を返す場合、その内側の関数はホストが評価するまで実行しない。

### 宣言の検証と配置

定義は公開された同期の静的関数とし、不正宣言は既存の `LYC001` で拒否する。非同期関数、参照戻り値、`ref`・`out`・`in`・`params` 引数、`ref struct`、非公開の引数型、予約名 `key`・`__` 始まりの引数を受け付けない。型制約は引き継ぐが、`allows ref struct` は受け付けない。生成名の重複はクラス方式も含めて検出し、オーバーロードには `Composable.Name` で別名を付ける。

契約は `Lumyte.Composition`、生成は `Lumyte.Composition.Generators` の既存パッケージに置く。契約は BCL のみへ依存し、生成は既存の Roslyn 構成を使う。関数は生成コードから型付きで呼び、実行時のリフレクションや `DynamicInvoke` を使わない。

### 公開 API と参照元

基準コミットの `src/Core/Lumyte.Composition/ComposableAttribute.cs`、`ComposeFunctionAdapterAttribute.cs`、`ICompositionRebuildable.cs` と、`src/Core/Lumyte.Composition.Generators/CompositionGenerator.Functions.cs` を参照する。以下は既存宣言と上記の例から生成されるメソッドの抜粋であり、基準からの差分はないため無印とする。

```diff
 namespace Lumyte.Composition
 {
     // クラスと静的関数を生成対象にできる。
     [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method,
         AllowMultiple = false, Inherited = false)]
     public sealed class ComposableAttribute : Attribute
     {
         public string? Factory { get; set; }
         public string? Name { get; set; }
     }
     [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
     public sealed class ComposeFunctionAdapterAttribute : Attribute
     {
         // 戻り値側に指定する。型の条件は「定義とホストの契約」に従う。
         public ComposeFunctionAdapterAttribute(Type adapterType);
         public Type AdapterType { get; }
     }
     public interface ICompositionRebuildable
     {
         // 新しい宣言を作る関数を保持する。ここでは実行しない。
         void SetCompositionFactory(Func<object> factory);
     }
 }
 namespace Lumyte.Composition.Sample
 {
     public static partial class Compose
     {
         // 例の定義から生成する。本文を実行せずホストの宣言を返す。
         public static Definitions.Widget Greeting(
             string? label = "hello", Action? onBuild = null, string? key = null);
         public static Definitions.Widget Typed<T>(T value, string? key = null)
             where T : notnull;
     }
 }
```

## 検討した代替案

- 通常の関数だけで即時構築する: 宣言用の基底クラスが不要で、その場で結果を使える。一方、ホストが状態を照合してから関数を評価する境界を作れない。
- 生成器に UI と状態管理を組み込む: 利用側の実装を減らせるが、Composition が実行環境へ依存する。型付きの宣言と呼び出しを生成し、実行責任はホストへ残す。

## 結果と影響

利用者は静的関数の書き方を保ち、評価時期と部品の識別を制御できる。ホスト用の基底クラスと評価処理が必要になり、状態の照合・寿命・例外への対応も利用者が担う。既存のクラス方式、静的操作、名前付きの子領域は維持する。

検証では UI に依存しない利用側コードをコンパイルし、既定値、明示した `null`、デリゲート、型制約、名前の重複、`LYC001` を確認する。基準コミットの `tests/Core/Lumyte.Composition.Tests/FunctionTests.cs` と `GeneratorTests.Functions.cs` が扱う実行の遅延、再構築、コールバック、`init` 専用キーを含め、既存の値引数変換と所有コレクションの契約も維持する。別アセンブリからの利用、NativeAOT、契約と生成器のパッケージ経由での利用を受け入れ条件とする。
