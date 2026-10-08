# ADR-COMPOSITION-0001: 宣言的なオブジェクト構築の契約ライブラリ

- 状態: 提案
- 日付: 2026-10-08

## 背景

オブジェクトの設定値と子要素を C# の式でまとめて記述し、階層構造を読みやすく構築したい。旧 Lumyte の `Lumyte.Composition` は、属性による宣言と、引数の省略を表す `Optional<T>` を提供する。別プロジェクトの Source Generator がその宣言からファクトリと子要素を設定するインデクサを生成する。

通常の省略可能引数では、省略した値と明示的な `0`、`false`、`null` を区別できない。構築対象が持つ初期値を維持しながら明示した値だけを適用するため、その区別を公開契約に含める必要がある。

現行 Lumyte には Composition の実装がない。旧ライブラリの契約を基礎に、描画や UI に依存せず利用できる基盤ライブラリとして導入する方針を提案する。

## 決定

### 対象範囲と配置

`src/Core/Lumyte.Composition/` に C# ライブラリを配置し、プロジェクト名、NuGet パッケージ名、名前空間を `Lumyte.Composition` とする。[ADR-0002](../0002-repository-layout.md) の基本型・基盤機能の分類に従う。

本 ADR は四つの属性と `Optional<T>` の公開契約、専用デリゲート型を返す静的プロパティ、`with` による追加設定、およびそれを消費するコード生成との境界を扱う。DI コンテナ、サービス探索、シリアライズ、差分更新、描画、子要素の所有権管理は提供しない。`Optional<T>` は Composition の引数表現として導入し、汎用的な結果型には拡張しない。

この PR は設計文書のみを追加する。[ADR-0001](../0001-adr-writing-policy.md) に従い、設計を採用してから実装する。

### 公開 API

比較元は現行 Lumyte の main（Composition API なし）であり、以下はすべて新規 API である。属性はすべて `AllowMultiple = false` とする。

```csharp
namespace Lumyte.Composition
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ComposableAttribute : Attribute
    {
        public ComposableAttribute();
        public string? Factory { get; set; }
        public string? Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class ComposeParameterAttribute : Attribute
    {
        public ComposeParameterAttribute();
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class ComposeContentAttribute : Attribute
    {
        public ComposeContentAttribute();
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class CompositionDefaultsAttribute : Attribute
    {
        public CompositionDefaultsAttribute(string factoryClass);
        public string FactoryClass { get; }
    }

    public readonly struct Optional<T>
    {
        public Optional(T value);
        public bool HasValue { get; }
        public T Value { get; }
        public static implicit operator Optional<T>(T value);
    }
}
```

| API | 役割・契約 |
| --- | --- |
| `ComposableAttribute()` | ファクトリ生成対象のクラスを明示する。派生クラスへの自動適用はしない。生成対象は `partial` とする。 |
| `ComposableAttribute.Factory` | 生成する静的ファクトリクラスの名前。既定値は `null`。省略時はアセンブリ既定値を使い、それもなければ `Compose` とする。 |
| `ComposableAttribute.Name` | 生成する静的プロパティの名前。既定値は `null`。省略時は対象クラス名を使う。 |
| `ComposeParameterAttribute()` | フィールドまたはプロパティをファクトリ引数の候補として指定する。値型、参照型、nullable 型を扱う。 |
| `ComposeContentAttribute()` | 子要素を設定するメンバーを指定する。ファクトリ引数とは別の役割であり、同一メンバーに両属性を指定しない。 |
| `CompositionDefaultsAttribute(string factoryClass)` | アセンブリ内のファクトリクラス名の既定値を指定する。`null`、空文字、空白のみなら `ArgumentException` を送出する。 |
| `CompositionDefaultsAttribute.FactoryClass` | コンストラクターで受け取った名前を変更せず返す。 |
| `Optional<T>(T value)` | 値を指定した状態を作る。`value` が `default(T)` でも `HasValue = true`。 |
| `Optional<T>.HasValue` | 引数が指定されたかを表す。`default(Optional<T>)` は `false`。 |
| `Optional<T>.Value` | 指定された値を返す。未指定なら `InvalidOperationException` を送出する。 |
| `implicit operator Optional<T>(T value)` | コンストラクターと同じ指定済み状態へ変換する。 |

`Optional<T>` に型制約を付けない。nullable reference types の通常の規則に従い、明示的な `null` は `Optional<string?>` などで表す。ラッパー自身はヒープ上の領域を確保しないが、参照型の値や生成した子要素配列の確保は別途発生する。

`Factory`、`Name` の setter は値を保持するだけとし、C# 識別子としての妥当性、空文字、名前衝突は Generator が診断する。`CompositionDefaultsAttribute` のコンストラクターによる検査も識別子検証の代わりにはならない。

### コード生成との責務分担

属性自体はインスタンス生成やメンバーへの代入を実行しない。`Lumyte.Composition` は BCL のみに依存し、Roslyn、Graphics、Engine、Platform、Native の参照を持たない。

デリゲート型・静的プロパティ・インデクサを実現するには、別の `Lumyte.Composition.Generators` をビルド時の Analyzer として利用側に導入する。利用側の通常参照は契約ライブラリへ向け、Roslyn や Generator を実行時依存に含めない。Generator の実装と配布は後続 ADR で設計するが、生成機能には以下の振る舞いを要求する。

- 非 required の設定値は `Optional<T> parameter = default` で受け取り、指定済みの場合だけ代入する。未指定ならフィールド初期化子や引数なしコンストラクターによる初期値を維持する。
- `required` の設定値は型 `T` の必須引数として受け取り、省略をコンパイル時に拒否する。`init` の代入は構築中に行う。未設定の required メンバーを `SetsRequiredMembers` だけで隠さない。
- 基底型に宣言された対象メンバーも走査する。派生型からアクセスできないメンバー、重複する引数名、書き込み不能なメンバーを診断する。`Inherited` の指定だけに依存して走査を省略しない。
- 子要素メンバーは継承階層全体で高々一つとする。旧実装と同様に、属性を継承しない `ComposeContentAttribute` でも基底型に宣言されたメンバーは走査対象になる。
- 子要素がある場合は `Component this[params Child[] content] { get; }` に相当するインデクサを生成する。呼び出しは指定順の配列を対象メンバーへ代入し、同じインスタンスを返す。追記ではなく置換であり、繰り返し呼ぶと直前の子要素を置き換える。
- 子要素メンバーの型は一次元配列、`IEnumerable<T>`、`IReadOnlyCollection<T>`、`IReadOnlyList<T>`、`ICollection<T>`、`IList<T>` を初期候補とする。`List<T>` などへ配列をそのまま代入しない。確定した対応型と不正入力の診断は後続 ADR に記録する。
- ファクトリの公開入口は、生成する専用デリゲート型を返す getter のみの静的プロパティとする。型ごとの指定、アセンブリ指定、`Compose` の順で外側のファクトリクラス名を解決する。通常の静的メソッドは公開入口として生成しない。

Generator の詳細が未採用の間、属性だけの導入を宣言的構築機能の実装完了とは扱わない。旧 Generator をそのまま移植することも決定しない。

### 専用デリゲート型と同名プロパティ

ファクトリごとに異なる名前付きデリゲート型を生成し、外側の静的クラスにその型の静的プロパティを配置する。例えば `Compose.Grid` の型を `Compose.Definitions.GridFactory`、`Compose.Text` の型を `Compose.Definitions.TextFactory` とする。`Func<...>` へ共通化せず、専用型を拡張メソッドの receiver にできるようにする。

利用側は `using static Example.Compose;` により `Grid()`、`Text()` と記述する。`Grid()` はプロパティが返すデリゲートの呼び出し、`Grid.Column(1)` は同じプロパティ値を receiver とする拡張メソッド呼び出しになる。拡張メソッドは名前空間スコープの非 generic な静的クラスで定義し、その名前空間を利用側で import する。`Column` をデリゲートの静的メンバーとして定義する必要はない。

省略可能引数、引数名、既定値、required 引数はデリゲートの `Invoke` シグネチャを決める宣言に含める。接続先メソッドだけに既定値を付けても `Grid()` の省略には使えない。プロパティは getter のみで、初期化時に生成した同じデリゲートを返す。取得だけでは構築や設定を実行せず、呼び出すたびに新しい対象インスタンスを作る。外部からファクトリを差し替える setter は設けない。

### 内部クラスへの定義の配置

構築対象のクラスは外側のファクトリクラス内の `Definitions` という内部クラスに配置し、プロパティ群は外側に生成する。`Compose.Definitions.Grid` と `Compose.Grid`、`Compose.Definitions.Text` と `Compose.Text` のように、定義するクラスと生成されるプロパティの名前を一致させる。`Definitions` は包含を表す名前であり、C# の `internal` アクセス修飾子を意味しない。外部利用を想定する定義型とデリゲート型は `public` とする。

外側は `public static partial class Compose`、内部は `public static partial class Definitions`、対象は `partial class` とする。Generator は包含型の名前と階層を維持して partial 宣言を生成する。この形の nested 型への対応は初期導入の必須条件とし、任意の深さや generic な包含型への対応とは分ける。プロパティ名は既定で対象クラス名を使用し、明示的な `Name` による別名は opt-in とする。同じ外側クラスへ集約する際のプロパティ名・補助型名・既存メンバーとの衝突は診断する。

### with による追加設定

生成デリゲートの最後の省略可能引数として `IReadOnlyList<Action<TComponent>>? @with = null` を設ける。利用側は `with: [ ... ]` と書ける。`with` は追加設定専用の予約引数名とし、`ComposeParameter` から生成される引数名との衝突は診断する。コレクション式を直接受け取れる型を使い、`Optional<IReadOnlyList<...>>` で包まない。

`Grid.Column(1)` のような拡張メソッドは設定値を捕捉した `Action<T>` を返し、その時点では子要素を変更しない。ファクトリは次の順序で処理する。

1. インスタンスの初期化子と引数なしコンストラクターを実行する。
2. 通常のファクトリ引数から required と指定済みの設定値を適用する。init メンバーは構築中に設定する。
3. `with` のアクションを列挙順に一度ずつ、作成した同じインスタンスへ適用する。
4. そのインスタンスを返す。続く子要素インデクサの処理はその後に実行される。

`with` の省略・null・空コレクションはすべて追加設定なしとする。null のアクション要素は、アクションの実行前に全要素を検査して `ArgumentException` で拒否する。アクションが例外を送出した場合は後続を実行せず、その例外を伝播し、インスタンスを返さない。適用済みの変更やコンストラクター・アクションの外部への副作用はロールバックしない。通常の設定と同じ書き込み可能な値を変更する場合は後から適用したアクションが優先される。アクションで init メンバーを書き換えたり、required 引数を省略したりはできない。

設定を共通基底型へ適用できる場合は `Action<T>` の反変性を利用する。例えば `Action<Node>` は `Action<Text>` として渡せる。`Grid.Column` の具体的な保存先や親 Grid による解釈は UI 側の責務であり、Core に Grid やレイアウトの型を追加しない。

### 利用例と生成される公開入口

以下は導入後を想定した定義と生成 API の例であり、この PR に実装済みの API ではない。UI 型は契約を説明するための仮の型とする。

```csharp
using System;
using System.Collections.Generic;
using Lumyte.Composition;

namespace Example;

public static partial class Compose
{
    public static partial class Definitions
    {
        public abstract class Node
        {
            public int GridColumn { get; set; }
        }

        [Composable]
        public partial class Grid : Node
        {
            [ComposeContent]
            public IReadOnlyList<Node> Children { get; set; } = [];
        }

        [Composable]
        public partial class Text : Node
        {
            [ComposeParameter]
            public string? Content { get; set; } = "untitled";
        }
    }
}
```

生成される公開入口の宣言は次の形になる。デリゲートを接続する非公開の構築処理とインデクサの本体は省略する。

```csharp
using System;
using System.Collections.Generic;
using Lumyte.Composition;

namespace Example;

public static partial class Compose
{
    public static Definitions.GridFactory Grid { get; }
    public static Definitions.TextFactory Text { get; }

    public static partial class Definitions
    {
        public delegate Grid GridFactory(
            IReadOnlyList<Action<Grid>>? @with = null);

        public delegate Text TextFactory(
            Optional<string?> content = default,
            IReadOnlyList<Action<Text>>? @with = null);

        public partial class Grid
        {
            public Grid this[params Node[] content] { get; }
        }
    }
}
```

拡張と利用は次のように記述する。`using static` で import するのは外側のクラスとし、`Definitions` の型を同時に static import して名前解決を曖昧にしない。

```csharp
using System;
using static Example.Compose;

namespace Example;

public static class GridFactoryExtensions
{
    public static Action<Compose.Definitions.Node> Column(
        this Compose.Definitions.GridFactory factory,
        int column)
    {
        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        return node => node.GridColumn = column;
    }
}

public static class Usage
{
    public static Compose.Definitions.Grid Build() =>
        Grid()[Text(with: [Grid.Column(1)])];
}
```

`Grid.Column(1)` の receiver は拡張の入口を識別するために使用し、Grid のインスタンスを生成しない。返された設定を `Text` の `with` へ渡し、構築した Text に column 1 を設定してから Grid の子要素へ配置する。別の型専用デリゲートにはこの拡張を適用できない。

`Text()` は Content の初期値を維持し、`Text(content: (string?)null)` は明示的な null を設定する。`default(Optional<string?>)` は未指定、`new Optional<string?>(null)` は指定済みであり、両者を区別する。

インデクサは対象インスタンスを変更するため、共有済みオブジェクトの並行変更は利用側で同期する。子要素への参照を設定しても、破棄責任の移譲、ディープコピー、親子関係の検証、スレッド安全性は保証しない。`with` のアクションも任意の利用側コードであり、スレッド安全性や副作用の隔離を保証しない。

### 対応環境と互換性

契約ライブラリは managed code のみとし、Windows、Linux、Browser で共通の API を使用する。実行時のリフレクションや動的コード生成を必須にせず、AOT と trimming を妨げる依存を導入しない。具体的な TargetFramework は導入時の共通 .NET 設定に合わせる。各環境の動作確認は実装後に行う。

旧ライブラリの公開型名、名前空間、属性の適用先、引数省略の意味を維持する。旧 Generator の静的メソッドを、専用デリゲート型を返す静的プロパティへ変更する。nested 型の配置と拡張のための型 identity も公開契約に含める。旧 Generator が生成するすべてのコードとのバイナリ互換性は保証せず、移行時は新 Generator で再生成する。ファクトリ引数の順序、名前変換、継承の詳細を確定するまでは、旧実装との完全互換を宣言しない。

## 検討した代替案

### 通常の静的ファクトリメソッドまたは共通の Func 型

静的メソッドは `Grid()` と呼べるが、そのメソッド自体を receiver として `Grid.Column(1)` の拡張を解決できない。共通の `Func` 型では同じシグネチャの別ファクトリにも拡張が適用されるため、ファクトリごとの専用デリゲート型と静的プロパティを使用する。

### 定義型とプロパティを同じクラス直下に配置する

C# では同じクラスに同名の nested 型とプロパティを宣言できない。型を `Definitions` にまとめ、プロパティを外側へ配置することで両方の名前を保つ。

### オブジェクト初期化子とコレクション初期化子だけを使用する

追加ライブラリなしで明示的に構築できるため、単純な用途では引き続き利用する。名前付き引数とインデクサによる階層表現を共通化し、非公開メンバーも宣言から設定する用途には生成用の契約を用意する。

### nullable 型や default 値を省略の印として使用する

`null`、`0`、`false` を設定できなくなるか、独自の sentinel 値が必要になる。`Optional<T>` に指定の有無を独立して保持する。

### 実行時のリフレクションや DI コンテナで構築する

属性の解釈と依存解決をまとめられる一方、今回必要なのは利用側が明示する値と子要素による構築である。実行時の探索、依存解決、AOT 対応の負担を追加しない。

### 契約と Generator を一つの実行時ライブラリにまとめる

導入対象を一つにできるが、Roslyn のビルド時依存と利用側の実行時依存を混在させる。契約とコード生成を分離し、配布時の導入補助は Generator の設計で検討する。

## 結果と影響

- 同名の定義クラスと静的プロパティを階層で分離し、`Grid()` と `Grid.Column(1)` を同じ入口から記述できる。
- 専用デリゲート型が拡張ポイントになるため、別のファクトリへ拡張が誤って適用されることを型で防げる。デリゲート型の変更も公開 API の変更として扱う。
- キャッシュしたデリゲート、with のコレクション、設定を捕捉するアクションの確保が発生し得る。性能評価ではこれらを含める。

- 構築対象の初期値を維持しつつ、default 値と null を明示的に設定できる。
- UI やシーンなどの具体的な型に依存せず、宣言的な構築の契約を共有できる。
- 属性の追加だけではファクトリやインデクサは使えず、別途 Generator の導入が必要になる。
- インデクサの getter が対象を変更するため、通常の参照操作とは意味が異なる。置換と同一インスタンスの返却を利用者へ説明する必要がある。
- 設定メンバーの追加・変更は生成ファクトリのシグネチャにも影響する。生成 API も互換性確認の対象になる。

## 検証方針

本 PR では ADR の必須項目、カテゴリ採番、相対リンク、旧公開 API との対応を確認する。実装時の受け入れ条件は以下とする。

- 四つの属性の適用先、重複指定の禁止、継承設定を確認する。
- `CompositionDefaultsAttribute` の通常値の保持と、null・空文字・空白の拒否を確認する。
- `Optional<T>` の未指定、通常値、0、false、nullable の null を区別し、未指定の `Value` で例外になることを確認する。
- `Grid()[Text(with: [Grid.Column(1)])]` の生成後コンパイルと実行、同名の型とプロパティの共存、外部アセンブリからの拡張、デリゲート宣言側の既定引数、プロパティ取得時の非構築と呼び出し時の新規構築を確認する。
- `with` の適用順・通常引数に対する優先・省略／null／空、null 要素の拒否、途中の例外伝播と後続の停止、予約名衝突、別ファクトリへの拡張の拒否を確認する。
- Generator の採用後は生成コードのコンパイルと実行で、初期値維持、required／init、継承、入れ子、子要素の順序・置換・同一インスタンス返却を確認する。
- 生成対象の不正な宣言を診断できること、実行時成果物が Roslyn に依存しないこと、Browser／AOT／trimming の対応を検証する。

上記は将来の検証条件であり、実装や環境ごとの検証は未実施である。

## 別途決定する事項

- Generator の配置、TargetFramework、Roslyn 対応バージョン、Analyzer／NuGet の配布方法。
- 引数なしコンストラクターの条件、generic・abstract 型、および本 ADR の二段構成以外の nested 型への対応。
- 必須引数と省略可能引数の順序、引数名の変換規則、継承・override・メンバー隠蔽の扱い。
- 名前の妥当性と衝突、既存インデクサとの衝突、static／readonly／init／required メンバーの診断と安全な生成。
- 子要素の対応型、null 配列・null 要素・空配列の扱い、配列参照を保持する際の aliasing 契約。
- インクリメンタル生成、診断 ID、生成コードの XML documentation と lint の適合。

## 参考資料

調査対象を旧リポジトリのコミット `462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee` に固定する。

- [旧 Lumyte.Composition の契約](https://github.com/ikihiki/Lumyte_old/tree/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition)
- [旧 Composition Generator](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Generators/CompositionGenerator.cs)
- [旧 Composition の利用テスト](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Tests/CompositionTests.cs)
