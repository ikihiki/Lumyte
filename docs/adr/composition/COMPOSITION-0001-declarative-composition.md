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

本 ADR は四つの属性と `Optional<T>` の公開契約、およびそれを消費するコード生成との境界を扱う。DI コンテナ、サービス探索、シリアライズ、差分更新、描画、子要素の所有権管理は提供しない。`Optional<T>` は Composition の引数表現として導入し、汎用的な結果型には拡張しない。

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
| `ComposableAttribute.Name` | 生成するメソッドの名前。既定値は `null`。省略時は対象クラス名を使う。 |
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

ファクトリとインデクサを実現するには、別の `Lumyte.Composition.Generators` をビルド時の Analyzer として利用側に導入する。利用側の通常参照は契約ライブラリへ向け、Roslyn や Generator を実行時依存に含めない。Generator の実装と配布は後続 ADR で設計するが、生成機能には以下の振る舞いを要求する。

- 非 required の設定値は `Optional<T> parameter = default` で受け取り、指定済みの場合だけ代入する。未指定ならフィールド初期化子や引数なしコンストラクターによる初期値を維持する。
- `required` の設定値は型 `T` の必須引数として受け取り、省略をコンパイル時に拒否する。`init` の代入は構築中に行う。未設定の required メンバーを `SetsRequiredMembers` だけで隠さない。
- 基底型に宣言された対象メンバーも走査する。派生型からアクセスできないメンバー、重複する引数名、書き込み不能なメンバーを診断する。`Inherited` の指定だけに依存して走査を省略しない。
- 子要素メンバーは継承階層全体で高々一つとする。旧実装と同様に、属性を継承しない `ComposeContentAttribute` でも基底型に宣言されたメンバーは走査対象になる。
- 子要素がある場合は `Component this[params Child[] content] { get; }` に相当するインデクサを生成する。呼び出しは指定順の配列を対象メンバーへ代入し、同じインスタンスを返す。追記ではなく置換であり、繰り返し呼ぶと直前の子要素を置き換える。
- 子要素メンバーの型は一次元配列、`IEnumerable<T>`、`IReadOnlyCollection<T>`、`IReadOnlyList<T>`、`ICollection<T>`、`IList<T>` を初期候補とする。`List<T>` などへ配列をそのまま代入しない。確定した対応型と不正入力の診断は後続 ADR に記録する。
- 生成ファクトリは対象型の名前空間に配置し、型ごとの指定、アセンブリ指定、`Compose` の順で名前を解決する。

Generator の詳細が未採用の間、属性だけの導入を宣言的構築機能の実装完了とは扱わない。旧 Generator をそのまま移植することも決定しない。

### 利用例と状態の区別

以下は Generator 導入後を想定した利用例であり、この PR に実装済みの API ではない。

```csharp
using Lumyte.Composition;
using static Example.Compose;

namespace Example;

[Composable]
public partial class Node
{
    [ComposeParameter]
    public string? Name { get; set; } = "untitled";

    [ComposeParameter]
    public bool Enabled { get; set; } = true;

    [ComposeContent]
    public IReadOnlyList<Node> Children { get; set; } = [];
}

// ファクトリの想定シグネチャ:
// public static Node Node(
//     Optional<string?> name = default,
//     Optional<bool> enabled = default);

public static class Usage
{
    public static Node Build() =>
        Node(name: "root")[
            Node(enabled: false),
            Node(name: (string?)null)
        ];
}
```

`Node()` は名前の初期値と `Enabled = true` を維持する。`Node(enabled: false)` は明示的な false を適用する。`Node(name: (string?)null)` は名前を null にする。`default(Optional<string?>)` は未指定、`new Optional<string?>(null)` は指定済みであり、両者を区別する。

ファクトリは毎回新しいインスタンスを作る。インデクサはそのインスタンスを変更するため、共有済みオブジェクトの並行変更は利用側で同期する。子要素への参照を設定しても、破棄責任の移譲、ディープコピー、親子関係の検証、スレッド安全性は保証しない。

### 対応環境と互換性

契約ライブラリは managed code のみとし、Windows、Linux、Browser で共通の API を使用する。実行時のリフレクションや動的コード生成を必須にせず、AOT と trimming を妨げる依存を導入しない。具体的な TargetFramework は導入時の共通 .NET 設定に合わせる。各環境の動作確認は実装後に行う。

旧ライブラリの公開型名、名前空間、属性の適用先、引数省略の意味を維持する。旧 Generator が生成するすべてのコードとのバイナリ互換性は保証せず、移行時は新 Generator で再生成する。ファクトリ引数の順序、名前変換、継承の詳細を確定するまでは、旧実装との完全互換を宣言しない。

## 検討した代替案

### オブジェクト初期化子とコレクション初期化子だけを使用する

追加ライブラリなしで明示的に構築できるため、単純な用途では引き続き利用する。名前付き引数とインデクサによる階層表現を共通化し、非公開メンバーも宣言から設定する用途には生成用の契約を用意する。

### nullable 型や default 値を省略の印として使用する

`null`、`0`、`false` を設定できなくなるか、独自の sentinel 値が必要になる。`Optional<T>` に指定の有無を独立して保持する。

### 実行時のリフレクションや DI コンテナで構築する

属性の解釈と依存解決をまとめられる一方、今回必要なのは利用側が明示する値と子要素による構築である。実行時の探索、依存解決、AOT 対応の負担を追加しない。

### 契約と Generator を一つの実行時ライブラリにまとめる

導入対象を一つにできるが、Roslyn のビルド時依存と利用側の実行時依存を混在させる。契約とコード生成を分離し、配布時の導入補助は Generator の設計で検討する。

## 結果と影響

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
- Generator の採用後は生成コードのコンパイルと実行で、初期値維持、required／init、継承、入れ子、子要素の順序・置換・同一インスタンス返却を確認する。
- 生成対象の不正な宣言を診断できること、実行時成果物が Roslyn に依存しないこと、Browser／AOT／trimming の対応を検証する。

上記は将来の検証条件であり、実装や環境ごとの検証は未実施である。

## 別途決定する事項

- Generator の配置、TargetFramework、Roslyn 対応バージョン、Analyzer／NuGet の配布方法。
- 対応するクラス形状、引数なしコンストラクターの条件、generic・nested・abstract 型の対応。
- 必須引数と省略可能引数の順序、引数名の変換規則、継承・override・メンバー隠蔽の扱い。
- 名前の妥当性と衝突、既存インデクサとの衝突、static／readonly／init／required メンバーの診断と安全な生成。
- 子要素の対応型、null 配列・null 要素・空配列の扱い、配列参照を保持する際の aliasing 契約。
- インクリメンタル生成、診断 ID、生成コードの XML documentation と lint の適合。

## 参考資料

調査対象を旧リポジトリのコミット `462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee` に固定する。

- [旧 Lumyte.Composition の契約](https://github.com/ikihiki/Lumyte_old/tree/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition)
- [旧 Composition Generator](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Generators/CompositionGenerator.cs)
- [旧 Composition の利用テスト](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Composition.Tests/CompositionTests.cs)
