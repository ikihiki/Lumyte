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

Composition に、引数を受け取って構成を返す `CompositionTemplate<TArgs, TComposition>` を追加する。テンプレートはラムダやメソッドグループを型付けする delegate とし、ラッパーオブジェクトを作らずに利用できる。Slot の入口がこの delegate 型を要求するインデクサーを返し、`Thumb()[thumb => ...]` のように入力型と戻り値型を文脈から決める。同じ入口は構築済みの構成も受け取り、`Thumb()[Text(...)]` と記述できる。

ここで構成とは、利用側が構造を表すために定義した戻り値である。`TComposition` は Widget などの利用側の型であり、Composition に共通ノード基底型や UI への依存を追加しない。

### 利用者の変更前・変更後

同じ人物データから文字列の構成を生成し、生成方法を受け渡す例を示す。変更前は BCL の関数を個別の契約として扱う。これは追加 API を使わない通常の C# による代替手段である。

```csharp
using System;

Func<Person, Label> personRow = person => new Label(person.Name);
Label first = personRow(new Person("Ada"));

public sealed record Person(string Name);
public sealed record Label(string Text);
```

変更後は今回追加するテンプレートを使う。上と同じ Person、Label を使い、生成される構成は変わらない。テンプレートの作成だけではラムダを実行しない。

```csharp
using Lumyte.Composition;

CompositionTemplate<Person, Label> personRow = person => new Label(person.Name);
Label first = personRow(new Person("Ada"));
```

関数の分割に専用属性は必要ない。通常の静的メソッド、インスタンスメソッド、ローカル関数、ラムダを、同じ delegate 型へ変換できる。複数の入力は `TArgs` に専用の型やタプルを使い、引数のない処理は ValueTuple を使う。一つのテンプレートは一つの戻り値を返す。複数の子を返す場合は利用側がコレクションなどの結果型と、その取り付け方法を定義する。

### テンプレートを型付けする Slot の定義

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

利用側は Item の指定箇所へラムダを直接書く。テンプレート用の変数や独立した構築関数は必要ない。以下は別の呼び出し側コードの抜粋であり、返された構成を画面へ接続する処理は省略する。

```csharp
using Lumyte.Composition;
using TemplateExample;
using static TemplateExample.Compose;

var list = ListView<Person>()[
    new Person("Ada"),
    new Person("Grace"),
    ListViewFactory<Person>().Item()[
        person => Text(content: person.Name)
    ]
];
```

`Item()[...]` は型付けされたラムダを捕捉した Slot 指定を返す。外側のインデクサーが定義側の Item を呼んで保存するだけで、人物ごとの生成は行わない。利用側のリスト処理が必要な時点で `list.ItemTemplate!(person)` を呼び、返された構成を取り付ける。

### Slider のノブをカスタマイズする利用案

UI ホストへの適用案として、Slider のノブを表す Thumb Slot にもテンプレートを渡す。基準コミットの `src/UI/Lumyte.UI.Controls/Compose.Slider.cs` は ThumbColor による色指定を提供し、`SliderElementBase.cs` の OnRecord がノブの矩形を直接描く。任意の構成をノブへ差し替える Slot はまだなく、以下の Thumb と SliderThumbContext は今回の提案である。

変更前は、値と色などを設定し、ノブの内容は組み込み描画へ任せる。

```csharp
using Controls = Lumyte.UI.Controls.Compose;

var slider = Controls.Slider(min: 0, max: 100, step: 1, value: 25);
```

変更後は、利用箇所でラムダを指定し、受理された値に応じてノブの表示を差し替える。`textLayoutProvider` は利用側が用意する既存の ITextLayoutProvider であり、その取得と画面への接続は省略する。

```csharp
using Lumyte.UI.Controls;
using Controls = Lumyte.UI.Controls.Compose;
using Media = Lumyte.UI.Media.Compose;

var slider = Controls.Slider(min: 0, max: 100, step: 1, value: 25)[
    Controls.Slider.Thumb()[
        thumb => Media.Text(
            value: thumb.Value >= 50 ? "◆" : "●",
            layoutProvider: textLayoutProvider)
    ]
];
```

値に応じた変更が不要なら、同じ Slot に構成を直接渡せる。以下は上と同じ using と textLayoutProvider を使う。

```csharp
var slider = Controls.Slider(min: 0, max: 100, step: 1, value: 25)[
    Controls.Slider.Thumb()[
        Media.Text(value: "◆", layoutProvider: textLayoutProvider)
    ]
];
```

直接指定では Media.Text の呼び出しがその場で一回実行され、作られた構成を捕捉する。後から値が変わってもこの式を再実行せず、引数を無視して同じ構成を返すテンプレートとして保存する。

この案の公開境界は次のとおりとする。UI 固有の状態と保存領域は UI.Controls に置き、Composition 契約へ Slider の知識を持ち込まない。

```diff
 namespace Lumyte.UI.Controls
 {
+    // 呼び出し時点で Slider が受理した値。宣言の初期値ではない。
+    public readonly record struct SliderThumbContext(double Value);

     public static partial class Compose
     {
         public static partial class Definitions
         {
             public partial class Slider
             {
+                // 未指定は null で、既存の ThumbColor による描画を使う。
+                public CompositionTemplate<SliderThumbContext,
+                    Lumyte.UI.Compose.Definitions.Widget>? ThumbTemplate
+                { get; private set; }
+
+                // 非公開の定義メソッド。適用時は保存だけを行う。
+                [ComposeSlot]
+                private static void Thumb(Slider target,
+                    CompositionTemplate<SliderThumbContext,
+                        Lumyte.UI.Compose.Definitions.Widget> template);
             }
         }
     }

+    public static class ComposeSliderCompositionExtensions
+    {
+        // 型付きラムダを受け取る入口。ここではテンプレートを呼ばない。
+        public static CompositionTemplateSlot<Compose.Definitions.Slider,
+            SliderThumbContext, Lumyte.UI.Compose.Definitions.Widget> Thumb(
+            this Compose.Definitions.SliderFactory factory);
+    }
 }
```

Slider の要素は初回接続時と受理値またはテンプレート参照の変更時に delegate を呼び、結果をノブ専用の子として保持する。描画のたびには評価せず、旧結果の更新・切り離しは UI ホストの所有権規則に従う。同じ宣言を複数の画面へ接続する場合も、各画面の要素が自身の受理値で評価し、接続先固有の子要素や状態を画面間で共有しない。固定の構成は同じ宣言を返すため、宣言自体を複数接続できるかは既存の UI ホストの所有権規則に従う。直接指定で複製や所有権制約の免除は行わない。

差し替えるのはノブの表示内容である。位置、入力用の当たり領域、ポインター捕捉、ドラッグ、キーボード操作、値の制約、OnChanged と OnCommitted は引き続き Slider が管理する。この適用案では既存のノブ矩形へ子を配置・クリップし、子の希望サイズでトラックや入力領域を変更しない。ノブの子は装飾として扱い、独立した入力対象・フォーカス対象・自動操作対象にはせず、Slider 自身の操作契約を維持する。

テンプレート未指定時は既存のノブを描き、指定時は組み込みノブの代わりに結果の構成を描く。ThumbColor は既存の組み込み描画にだけ作用し、カスタム構成の色を暗黙に上書きしない。

### 評価、状態、所有権

delegate の作成・Slot 指定の作成・Slot 適用では生成関数を呼ばない。通常の単一ラムダの呼び出しでは同期的に本体を一回実行し、入力と戻り値をそのまま渡す。コピー、キャッシュ、結果の検証、変更通知への購読、スレッド切り替えは行わず、関数の例外はそのまま伝播する。マルチキャストを組み合わせた場合も通常の delegate と同じく登録順に実行し、最後の戻り値を使い、例外で後続を停止する。

同じ引数で再評価しても、新しい構成になるとは保証しない。関数が新規作成するか既存値を返すかを決め、利用側が結果の同一性、キー、再利用、親子関係、破棄を管理する。再評価で以前の結果を自動破棄しない。戻り値の null 許容性は型注釈で表し、実行時に追加の null 検査はしない。

入力もクロージャーも参照先の状態を固定しない。テンプレートは関数とその捕捉値を保持するため、利用側は保持期間、再入、並行呼び出し、副作用を管理する。非同期完了を待つ仕組みは設けない。Task などを結果型として使用しても、Composition は通常の戻り値として渡すだけであり、構成への取り付けや完了待ちは利用側が決める。

### 構成の直接指定と共有

直接指定した式は C# の通常の評価順に従い、インデクサーへ渡す前に評価される。Slot 指定の作成・適用ではその式を再実行せず、捕捉済みの構成を返す delegate を保存する。再評価や同じ Slot 指定の別対象への適用でも構成自体を複製しない。

固定の構成を複数項目・複数接続先で使用できるかは、構成型とホストの所有権契約で決まる。接続先ごとに新しい構成が必要なら、`Thumb()[_ => Text(...)]` のようにラムダ内で構築する。Composition は構築済みノードの複製や親の付け替えを自動で行わない。

直接指定の null と delegate の null はいずれもインデクサーで ArgumentNullException とする。二つの参照型オーバーロードのどちらか決められない裸の null は、C# の通常のオーバーロード解決に従うため受理を保証しない。検証時は各引数型へ明示的にキャストして入口の拒否を確認する。これは delegate が評価後に null を返す場合の契約とは区別する。

### 関数コンポーネントとの関係

通常の構築関数をテンプレートへ代入する場合、delegate の呼び出しがその関数を直接実行する。一方、既存の `[Composable]` 関数から生成されたファクトリをラムダ内で呼ぶ場合は、そのファクトリが返す遅延宣言をテンプレートの結果とする。テンプレートは宣言を再帰的に評価しない。

例えば基準コミットの `samples/Lumyte.Composition.Sample/Compose.cs` の Greeting を使う場合、`CompositionTemplate<string, Compose.Definitions.Widget> greeting = label => Compose.Greeting(label: label);` は `greeting(label)` の呼び出し時に関数宣言を作る。定義関数本体を実行するのは既存のホストによる評価時であり、キーや再構築の規則は変更しない。生成ファクトリには追加の引数があるため、ラムダで必要な引数を対応付ける。

### 公開 API と生成契約の差分

比較元は冒頭の基準コミットとする。契約型は既存の `Lumyte.Composition`、生成処理は `Lumyte.Composition.Generators` に置き、BCL のみへ依存する。次は新規 API と、既存属性の説明の変更である。

```diff
 namespace Lumyte.Composition
 {
+    // ラムダ・メソッドグループを型付けする。UI や基底型の制約はない。
+    // null/default の入力、戻り値、例外は通常の delegate 呼び出しに従う。
+    public delegate TComposition CompositionTemplate<TArgs, TComposition>(TArgs args);
+
+    // 名前付きのテンプレート操作の入口。結果や保存領域は持たない。
+    public sealed class CompositionTemplateSlot<TTarget, TArgs, TComposition>
+        where TTarget : class
+    {
+        // apply が null なら ArgumentNullException。ここでは実行しない。
+        public CompositionTemplateSlot(
+            Action<TTarget, CompositionTemplate<TArgs, TComposition>> apply);
+        // template が null なら ArgumentNullException。delegate を実行せず捕捉する。
+        // 適用時に同じ delegate を apply へ渡す再利用可能な指定を返す。
+        public CompositionSlotAssignment<TTarget> this[
+            CompositionTemplate<TArgs, TComposition> template] { get; }
+        // 構築済みの構成を捕捉する。null なら ArgumentNullException。
+        // 引数を無視し、同じ composition を返す delegate の指定を返す。
+        public CompositionSlotAssignment<TTarget> this[
+            TComposition composition] { get; }
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

生成するテンプレート Slot 入口は次の形とし、ジェネリック制約を引き継ぐ。既存の CompositionSlotAssignment とインデクサーを使い、構成の取り付け先や保存辞書は追加しない。入口のインデクサーは一つの delegate または一つの構成を受け取り、複数引数や空指定は受け付けない。

```diff
 namespace TemplateExample
 {
+    public static class ComposeListViewCompositionExtensions
+    {
+        // インデクサーが delegate を型付けする入口。評価は行わない。
+        public static CompositionTemplateSlot<Compose.Definitions.ListView<T>,
+            T, Compose.Definitions.Widget> Item<T>(
+            this Compose.Definitions.ListViewFactory<T> factory)
+            where T : notnull;
+    }
 }
```

既存の子コレクション Slot は `Name()[children]` のまま維持し、テンプレート Slot は `Name()[args => composition]` とする。生成器は定義側の第二引数が CompositionTemplate の型定義に一致することを確認し、両型引数を入口の型へ引き継ぐ。インデクサーはその閉じた delegate 型を直接受けるため、C# がラムダの引数と結果を検査する。型付き変数やメソッドグループも同じインデクサーへ渡せる。構成を直接指定するオーバーロードは、引数を使わず同じ構成を返す delegate へ正規化し、定義側の Slot メソッドは引き続き CompositionTemplate だけを受け取る。Func や任意の delegate を Slot 定義として自動認識せず、ラッパーへの暗黙変換も必要ない。

Slot 定義の同期・非ジェネリック・static void、対象型を先頭引数にする制約、公開可能な引数型、名前衝突の検査は維持する。第二引数にテンプレートを許可する場合も、非対応宣言は既存の LYC001 で拒否し、実行時のリフレクションや DynamicInvoke を使わない。定義クラス自体の型引数はテンプレート型にも使用できる。

適用順序、同じ Slot への複数指定、失敗時の後続停止と非ロールバックは既存の Slot 契約を維持する。適用ごとに同じテンプレート参照を渡し、テンプレートや捕捉値を複製しない。テンプレートの解除が必要な部品は、別の明示的な操作を提供する。ComposeParameter に指定する場合は既存の delegate 引数と同じ規則を使い、値引数 adapter やラッパーへの変換を挟まず、その delegate 型を受け取る。テンプレート固有の自動評価は追加しない。

## 検討した代替案

- 部品ごとの Func だけを使う: 通常の C# だけで表せるが、構成を生成する値の役割と Slot の受け渡し契約が部品ごとに分散する。明示的なテンプレート型を共通契約にする。
- テンプレートをクラスで包み、Name(template) で渡す: 評価メソッドを提供できるが、利用箇所でラッパーの構築と型引数を記述する必要がある。delegate の型付けと Slot インデクサーを使い、ラムダを直接記述できる形を選ぶ。
- 新しい関数属性からテンプレートを生成する: 定型的な包み方を省けるが、既存の通常関数と Composable 関数の評価境界に別の生成規則が増える。型付き delegate を直接使い、追加の関数宣言生成は行わない。
- 共通の Composition ノード基底型を追加する: 結果型を統一できるが、利用側が結果型と所有権を定める既存の責務を変更する。型引数で結果を表す。

## 結果と影響

利用者は引数から構成を返す処理を通常の関数に分け、独立したテンプレートとして保持・受け渡しできる。既存の関数コンポーネントや子要素 Slot はそのまま使え、テンプレートの評価と結果の管理は利用側へ残る。

delegate の型引数は不変とし、異なる TComposition 間のテンプレート変換は自動提供しない。必要なら期待する delegate 型にラムダやメソッドグループを代入する。テンプレートは省略可能値や Slot の保存領域そのものではなく、未設定時の表示や再評価条件は受け取る部品が決める。

受け入れ条件は、UI 非依存の型でラムダ・メソッドグループ・ローカル関数・タプル入力をコンパイルできること、作成時には未実行で単一ラムダの呼び出しごとに一回実行されること、入力と結果の null/default および例外が契約どおり扱われることとする。Slot ではラムダの引数と戻り値の推論、構成の直接指定、対象型と両型引数の不一致の拒否、適用順序・再利用・null の拒否・通常内容との併用・LYC001・別アセンブリからの利用を確認する。

既存の子 Slot、ComposeParameter、引数 adapter、所有コレクション、関数宣言と再構築のテストを維持し、テンプレートの呼び出しが遅延宣言の評価まで進めないことを確認する。NativeAOT と trimming で実行時探索なしに呼び出せることも受け入れ条件とする。

直接指定では構築式が指定時に一回だけ実行されること、delegate の呼び出しや再適用では再構築されず同じ結果を返すこと、型付き null の拒否、既存の所有権制約の維持を確認する。

Slider への適用では、未指定時の既定描画、受理値によるラムダの再評価、描画のみでの非評価、複数画面での所有権の分離、値またはテンプレート変更時の子の更新と切り離し、既存のノブ領域内への配置を確認する。カスタムノブが入力を奪わず、ドラッグ・キーボード・値の制約・通知・自動操作の既存契約を維持することを受け入れ条件とする。
