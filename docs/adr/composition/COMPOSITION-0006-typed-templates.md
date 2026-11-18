# ADR-COMPOSITION-0006: 引数から構成を返す型付きテンプレートを関数として定義する

- 状態: 提案
- 日付: 2026-11-18
- 基準コミット: `7bcf1bc256e226d04d7e2818893035be73ab9d2e`
- 変更関係: 一部変更
- 変更元: [ADR-COMPOSITION-0001](COMPOSITION-0001-declarative-composition.md#名前付きスロット)の「名前付きスロット」
- 変更対象: ComposeSlot の第二引数に、子要素コレクションに加えて型付きテンプレートを許可する
- 継続する判断: 子要素 Slot の構文、適用順序、対象型の制限、保存領域と所有権を利用側に置く責務

## 背景

部品の利用者は、項目などの引数から構成を作る処理を、ラムダや独立した関数に分けて再利用したい。名前付き Slot は構築済みの子要素を受け取れるが、引数を受け取って構成を返す値を表す共通契約がなく、部品ごとに生成用の関数型と受け渡し方法を用意する必要がある。

基準コミットの `src/Core/Lumyte.Composition/CompositionSlot.cs` は子配列を捕捉し、`CompositionSlotAssignment<TTarget>` の適用時に操作へ渡す。`src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` は ComposeSlot の第二引数を対応する子コレクションに限定する。[ADR-COMPOSITION-0005 の「定義とホストの契約」](COMPOSITION-0005-function-factories.md#定義とホストの契約)は構築関数の遅延宣言を定めるが、引数を後から受け取るテンプレートそのものとは区別する。

## 決定

Composition に、引数を受け取って構成を返す `CompositionTemplate<TArgs, TComposition>` を追加する。テンプレートは Slot に依存しない再利用可能な値とし、通常の C# 関数やラムダから定義し、利用側が明示的に評価する。

ここで構成とは、利用側が構造を表すために定義した戻り値である。`TComposition` は Widget などの利用側の型であり、Composition に共通ノード基底型や UI への依存を追加しない。

### 利用者の変更前・変更後

同じ人物データから文字列の構成を生成し、生成方法を受け渡す例を示す。変更前は BCL の関数を個別の契約として扱う。これは追加 API を使わない通常の C# による代替手段である。

```csharp
using System;

Func<Person, Label> personRow = BuildPersonRow;
Label first = personRow(new Person("Ada"));

static Label BuildPersonRow(Person person) => new(person.Name);

public sealed record Person(string Name);
public sealed record Label(string Text);
```

変更後は今回追加するテンプレートを使う。上と同じ Person、Label、BuildPersonRow を使い、生成される構成は変わらない。テンプレートの作成だけでは関数を実行しない。

```csharp
using Lumyte.Composition;

var personRow = new CompositionTemplate<Person, Label>(BuildPersonRow);
Label first = personRow.Build(new Person("Ada"));

var inlineRow = new CompositionTemplate<Person, Label>(
    person => new Label(person.Name));
```

関数の分割に専用属性は必要ない。通常の静的メソッド、インスタンスメソッド、ローカル関数、ラムダを、同じ型付き関数として受け取る。複数の入力は `TArgs` に専用の型やタプルを使い、引数のない処理は ValueTuple を使う。一つのテンプレートは一つの戻り値を返す。複数の子を返す場合は利用側がコレクションなどの結果型と、その取り付け方法を定義する。

### テンプレートを受け取る Slot の定義

次は変更後の定義例である。`TemplateExample.Compose.Definitions` 内の公開された部分クラスとして、以下の型を定義する。ファクトリとインデクサーは既存の生成規則に従い、Item のテンプレート引数だけが今回の拡張である。

```csharp
using System.Collections.Generic;
using Lumyte.Composition;

namespace TemplateExample;

public sealed record Person(string Name);

public static partial class Compose
{
    public static partial class Definitions
    {
        public abstract class Widget { }

        [Composable]
        public partial class Text : Widget
        {
            [ComposeParameter]
            public string Content { get; init; } = "";
        }

        [Composable]
        public partial class ListView<T> : Widget where T : notnull
        {
            [ComposeContent]
            public IReadOnlyList<T> Items { get; set; } = [];

            public CompositionTemplate<T, Widget>? ItemTemplate
            {
                get;
                private set;
            }

            [ComposeSlot]
            private static void Item(
                ListView<T> target, CompositionTemplate<T, Widget> template)
                => target.ItemTemplate = template;
        }
    }
}
```

利用側は通常の関数をテンプレートに包んで Slot へ渡す。以下は別の呼び出し側コードの抜粋であり、返された構成を画面へ接続する処理は省略する。

```csharp
using Lumyte.Composition;
using TemplateExample;
using static TemplateExample.Compose;

var personRow = new CompositionTemplate<Person, Compose.Definitions.Widget>(
    BuildPersonRow);

var list = ListView<Person>()[
    new Person("Ada"),
    ListViewFactory<Person>().Item(personRow)
];

static Compose.Definitions.Widget BuildPersonRow(Person person)
    => Text(content: person.Name);
```

`Item(personRow)` はテンプレートを捕捉した Slot 指定を返す。外側のインデクサーが定義側の Item を呼んで保存するだけで、人物ごとの生成は行わない。利用側のリスト処理が必要な時点で `list.ItemTemplate!.Build(person)` を呼び、返された構成を取り付ける。

### 評価、状態、所有権

テンプレートの作成・Slot 指定の作成・Slot 適用では生成関数を呼ばない。`Build` 一回につき同期的に関数を一回呼び、入力と戻り値をそのまま渡す。コピー、キャッシュ、結果の検証、変更通知への購読、スレッド切り替えは行わず、関数の例外はそのまま伝播する。

同じ引数で再評価しても、新しい構成になるとは保証しない。関数が新規作成するか既存値を返すかを決め、利用側が結果の同一性、キー、再利用、親子関係、破棄を管理する。再評価で以前の結果を自動破棄しない。戻り値の null 許容性は型注釈で表し、実行時に追加の null 検査はしない。

入力もクロージャーも参照先の状態を固定しない。テンプレートは関数とその捕捉値を保持するため、利用側は保持期間、再入、並行呼び出し、副作用を管理する。非同期完了を待つ仕組みは設けない。Task などを結果型として使用しても、Composition は通常の戻り値として渡すだけであり、構成への取り付けや完了待ちは利用側が決める。

### 関数コンポーネントとの関係

通常の構築関数をテンプレートへ渡す場合、Build はその関数を直接実行する。一方、既存の `[Composable]` 関数から生成されたファクトリをラムダ内で呼ぶ場合は、そのファクトリが返す遅延宣言をテンプレートの結果とする。テンプレートは宣言を再帰的に評価しない。

例えば基準コミットの `samples/Lumyte.Composition.Sample/Compose.cs` の Greeting を使う場合、`new CompositionTemplate<string, Compose.Definitions.Widget>(label => Compose.Greeting(label: label))` は Build 時に関数宣言を作る。定義関数本体を実行するのは既存のホストによる評価時であり、キーや再構築の規則は変更しない。生成ファクトリには追加の引数があるため、ラムダで必要な引数を対応付ける。

### 公開 API と生成契約の差分

比較元は冒頭の基準コミットとする。契約型は既存の `Lumyte.Composition`、生成処理は `Lumyte.Composition.Generators` に置き、BCL のみへ依存する。次は新規 API と、既存属性の説明の変更である。

```diff
 namespace Lumyte.Composition
 {
+    // 引数から利用側の構成を作る。両型引数に UI や基底型の制約はない。
+    public sealed class CompositionTemplate<TArgs, TComposition>
+    {
+        // build が null なら ArgumentNullException。ここでは実行しない。
+        public CompositionTemplate(Func<TArgs, TComposition> build);
+        // 関数を一回呼ぶ。null/default の入力もそのまま渡す。
+        // 戻り値と例外をそのまま返し、評価済みの値を保持しない。
+        public TComposition Build(TArgs args);
+    }

-    // コンポーネントと名前付きの子コレクションを受け取る静的メソッド。
+    // 第二引数に対応する子コレクション、または
+    // CompositionTemplate<TArgs, TComposition> を受け取る静的メソッド。
     [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
     public sealed class ComposeSlotAttribute : Attribute
     {
     }
 }
```

生成するテンプレート Slot 入口は次の形とし、ジェネリック制約を引き継ぐ。既存の CompositionSlotAssignment とインデクサーを使い、テンプレート専用の取り付け先や保存辞書は追加しない。

```diff
 namespace TemplateExample
 {
+    public static class ComposeListViewCompositionExtensions
+    {
+        // template が null なら入口で ArgumentNullException。Build は呼ばない。
+        // 適用時に同じ template を定義側の Item へ渡す指定を返す。
+        public static CompositionSlotAssignment<Compose.Definitions.ListView<T>> Item<T>(
+            this Compose.Definitions.ListViewFactory<T> factory,
+            CompositionTemplate<T, Compose.Definitions.Widget> template)
+            where T : notnull;
+    }
 }
```

既存の子コレクション Slot は `Name()[children]` のまま維持し、テンプレート Slot は `Name(template)` とする。テンプレート型は専用の型定義との一致で判定し、Func や任意の delegate をテンプレート Slot として自動認識しない。ラムダやメソッドグループは明示的にテンプレートのコンストラクターへ渡す。

Slot 定義の同期・非ジェネリック・static void、対象型を先頭引数にする制約、公開可能な引数型、名前衝突の検査は維持する。第二引数にテンプレートを許可する場合も、非対応宣言は既存の LYC001 で拒否し、実行時のリフレクションや DynamicInvoke を使わない。定義クラス自体の型引数はテンプレート型にも使用できる。

適用順序、同じ Slot への複数指定、失敗時の後続停止と非ロールバックは既存の Slot 契約を維持する。適用ごとに同じテンプレート参照を渡し、テンプレートや捕捉値を複製しない。テンプレートの解除が必要な部品は、別の明示的な操作を提供する。ComposeParameter に指定する場合は通常の値型と同じ既存規則を使い、テンプレートに固有の引数変換や自動評価は追加しない。

## 検討した代替案

- 部品ごとの Func だけを使う: 通常の C# だけで表せるが、構成を生成する値の役割と Slot の受け渡し契約が部品ごとに分散する。明示的なテンプレート型を共通契約にする。
- ラムダを子要素 Slot のインデクサーで受け取る: 固定内容と表記を統一できるが、構築済みの子配列と生成処理の違いが読み取りづらい。テンプレートを値として定義し、丸括弧で渡す。
- 新しい関数属性からテンプレートを生成する: 定型的な包み方を省けるが、既存の通常関数と Composable 関数の評価境界に別の生成規則が増える。まずは型付き関数をそのまま包み、追加の宣言生成は行わない。
- 共通の Composition ノード基底型を追加する: 結果型を統一できるが、利用側が結果型と所有権を定める既存の責務を変更する。型引数で結果を表す。

## 結果と影響

利用者は引数から構成を返す処理を通常の関数に分け、独立したテンプレートとして保持・受け渡しできる。既存の関数コンポーネントや子要素 Slot はそのまま使え、テンプレートの評価と結果の管理は利用側へ残る。

型は不変とし、異なる TComposition 間のテンプレート変換は自動提供しない。必要なら構築関数を期待する戻り値型のテンプレートへ包み直す。テンプレートは省略可能値や Slot の保存領域そのものではなく、未設定時の表示や再評価条件は受け取る部品が決める。

受け入れ条件は、UI 非依存の型でラムダ・メソッドグループ・ローカル関数・タプル入力をコンパイルできること、作成時には未実行で Build ごとに一回実行されること、入力と結果の null/default および例外が契約どおり扱われることとする。Slot では対象型と両型引数の不一致を拒否し、適用順序・再利用・null の拒否・通常内容との併用・LYC001・別アセンブリからの利用を確認する。

既存の子 Slot、ComposeParameter、引数 adapter、所有コレクション、関数宣言と再構築のテストを維持し、テンプレートの Build が遅延宣言の評価まで進めないことを確認する。NativeAOT と trimming で実行時探索なしに呼び出せることも受け入れ条件とする。
