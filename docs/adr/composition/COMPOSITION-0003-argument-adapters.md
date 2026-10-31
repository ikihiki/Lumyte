# ADR-COMPOSITION-0003: 値引数adapterの生成時指定

- 状態: 採用・実装済み（main向けComposition拡張PR）
- 日付: 2026-10-30
- 対応: main向けComposition拡張PR、前提: COMPOSITION-0001・COMPOSITION-0002

## 背景

部品の名前付き引数へ、通常値・値ソース・補間文字列などを直接渡したい。宣言されたパラメーター型と呼出し側の入力形式を分離し、元のusing static構文を維持するため、生成引数を利用側の型付きadapterへ委譲するopt-in契約を追加する。

Compositionは利用側の値ソースや実行基盤の型を参照せず、adapterの値解決と対象への付加情報登録だけを呼び出す。購読、描画、親子関係や対象の寿命は利用側の責務とする。

## 決定

### 配置と公開API

既存Lumyte.CompositionとComposition.Generatorsを拡張する。契約はBCLのみ、Generatorは既存と同じRoslyn基準を使う。比較元はmain `4773a2b73a3a9a1a2b1d0b5e6c99364eaf070150`と[COMPOSITION-0002](COMPOSITION-0002-callback-parameters.md)のAPIである。

```diff
+namespace Lumyte.Composition
+{
+    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
+    public sealed class ComposeArgumentAdapterAttribute : Attribute
+    {
+        // publicなgeneric structの未構築型。閉じる型引数は対象propertyの型。
+        public ComposeArgumentAdapterAttribute(Type adapterType);
+        // 宣言型ごとの指定。完全一致またはgeneric定義で選択する。
+        public ComposeArgumentAdapterAttribute(Type parameterType, Type adapterType);
+        public Type? ParameterType { get; }
+        public Type AdapterType { get; }
+    }
+    public interface ICompositionRebuildable
+    {
+        void SetCompositionFactory(Func<object> factory);
+    }
+    public interface IComposeArgument<T>
+    {
+        // defaultのadapterはfalse、明示したdefault/nullはtrueとする。
+        bool IsSpecified { get; }
+        // 指定済みの初期値を同期解決する。未指定では呼ばない。
+        T GetInitialValue();
+        // factory成功後、再適用や寿命に必要な情報を対象へ登録する。
+        // setterは該当member専用の生成static delegate。ここでは初期setterを再実行しない。
+        void AttachTo<TTarget>(TTarget target, object memberKey, T initialValue,
+            Action<TTarget, T> setter) where TTarget : class;
+    }
+}
```

attributeは定義classまたは基底classへ付ける。Generatorが継承階層と対象の宣言型から採用するadapterを決定し、同じ対象への矛盾する複数指定はLYC001 errorとする。adapter指定のないcomponentでは現行生成APIを変えない。具体的なadapterの実装・購読・寿命は利用側が提供する。Compositionに利用側の実行基盤への参照を追加しない。

### 引数の生成規則

変更可能な非delegateのComposeParameterだけを対象にする。propertyの宣言型に対応するadapterを選び、optional引数はそのstructのdefault、requiredな変更可能値は省略不能なadapter引数とする。Optionalを外側に重ねず、二段のuser-defined conversionを要求しない。

init-only値は後から変更できないためadapterを使わず、元の値型/Optional規則を維持する。delegate型は[COMPOSITION-0002](COMPOSITION-0002-callback-parameters.md)に従う元のdelegate引数とし、lambdaをstructへ変換させない。content、with、操作、slotは従来の型と表記を維持する。

元のfactoryにadapter版のoverloadを追加せず、opt-inしたcomponentの専用delegateの引数型を置き換える。UIは提案段階でのAPI拡張として扱う。明示的にOptional型を指定して呼んでいたコードは移行対象であり、全consumerの無変更互換を宣言しない。

### 生成処理と失敗

1. required adapterのIsSpecifiedを全件確認する。未指定はWidget等を作る前にArgumentExceptionとする。
2. 指定済みadapterのGetInitialValueを生成引数順に一回ずつ呼び、初期値をlocalへ保持する。失敗では対象を生成しない。
3. 解決した具体値を使い、従来の生成constructorでrequired/initと通常parameterを設定する。withは従来どおりその後に実行する。
4. 全て成功したら指定済みadapterのAttachToを引数順に呼び、元の対象instanceを返す。memberKeyは生成memberごとのcached object、setterは定義型内に生成した型付きstatic helperを使う。アクセス可能性とsetterの制約を引き継ぐ。

AttachToは対象自身が所有する受動的な設定情報だけを登録し、外部購読、thread起動、UI接続、global registryへの登録を開始しない。後続AttachToで失敗しても外部から対象を保持する資源がなく、未返却対象はGC可能となる。constructor/withの外部副作用や、利用者がそこから公開した対象への変更をrollbackしたとは扱わない。withの後も初期値localを渡し、withが上書きした値との優先順位はadapter側で定義する。

再構築を必要とする対象は追加の`ICompositionRebuildable.SetCompositionFactory(Func<object> factory)`を実装する。adapter指定のfactoryはAttachTo成功後に、元の引数とwithから新しい同型宣言を構築する受動的recipeを一回登録する。recipeを実行するのは利用側であり、Generatorは購読を開始しない。init、callback、withも再実行され、withの優先順位を保つ。子のindexerはfactoryの後なのでrecipeには含めず、利用側が登録済みslot構造と子recipeを組み合わせる。手書きnewを含む動的ツリーの再構築方法も利用側が定義する。

指定形式に合わない型（ref structやgeneric外側型を持つadapterを含む）、選択した閉じた型が宣言型に一致するIComposeArgumentを実装しない場合、型制約不一致、非対応member、アクセス不能、生成名衝突は既存LYC001のerrorで拒否する。adapterのconstructorやGetInitialValueをGenerator自身が実行して検証しない。実行時reflection、DynamicInvoke、新しいruntimeの作成は行わない。

## コード例と実行時の動き

### UIに依存しない最小例

次の`Source<T>`は値の読み直しを説明するための単純な箱で、変更通知は持たない。`Arg<T>`は通常値またはその箱を受け取る利用側のadapterである。この例ではhostが明示的に再構築するため、`AttachTo`に保存する付加情報はない。

```csharp
using Lumyte.Composition;

namespace AdapterExample;

public sealed class Source<T>(T value)
{
    public T Value { get; set; } = value;
}

public readonly struct Arg<T> : IComposeArgument<T>
{
    private readonly T _constant;
    private readonly Source<T>? _source;

    private Arg(T constant, Source<T>? source)
    {
        _constant = constant;
        _source = source;
        IsSpecified = true;
    }

    public bool IsSpecified { get; }

    public static implicit operator Arg<T>(T value) => new(value, null);

    public static implicit operator Arg<T>(Source<T> source) => new(default!, source);

    public T GetInitialValue() => _source is { } source ? source.Value : _constant;

    public void AttachTo<TTarget>(TTarget target, object memberKey, T initialValue,
        Action<TTarget, T> setter) where TTarget : class
    {
        // この例では購読もsetterの再実行も行わない。
    }
}

public static partial class Compose
{
    public static partial class Definitions
    {
        [Composable, ComposeArgumentAdapter(typeof(Arg<>))]
        public partial class Gauge : ICompositionRebuildable
        {
            [ComposeParameter]
            public int Value { get; set; } = 10;

            [ComposeParameter]
            public string? Label { get; set; } = "default label";

            [ComposeParameter]
            public Action? OnClick { get; set; }

            public Func<object> Factory { get; private set; } = null!;

            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory)
                => Factory = factory;
        }
    }
}
```

この定義から生成されたfactoryは、`value`を`Arg<int>`、`label`を`Arg<string?>`で受け取る。delegateの`onClick`は`Action?`のままなので、lambdaをそのまま渡せる。利用側のコードは次のようになる。

```csharp
using AdapterExample;
using static AdapterExample.Compose;

var source = new Source<int>(20);
int clicks = 0;
var first = Gauge(value: source, onClick: () => clicks++);
// first.Value == 20。GetInitialValue → 構築 → AttachTo → recipe登録が完了。

source.Value = 30;
// first.Valueは20のまま。Compositionは購読や既存宣言の変更を行わない。

var next = (Compose.Definitions.Gauge)first.Factory();
// hostがrecipeを実行したので、同じsourceを読み直し、next.Value == 30。
// ReferenceEquals(first, next) == false。first.Valueは20のまま。
next.OnClick!(); // clicks == 1。callbackも引き継がれる。

var omitted = Gauge();                          // Value == 10, Label == "default label"
var explicitZero = Gauge(value: 0);             // Value == 0
var explicitNull = Gauge(label: (string?)null); // Label == null
var omittedAdapter = Gauge(value: default(Arg<int>)); // Value == 10
```

`default(Arg<int>)`は未指定だが、`0`からの暗黙変換では`IsSpecified == true`になる。明示した`null`も同じく指定済みである。requiredな変更可能propertyでは未指定adapterが例外になり、init-only propertyはadapterを使わず、通常の値を受け取る。

`with`を指定した場合は、初期property設定の後、`AttachTo`の前に実行される。recipe実行でも同じ`with`が再実行されるため、そこに書いた副作用も繰り返される。`AttachTo`に渡す`initialValue`は`with`実行前に読み取った値である。

### 宣言型と入力形式を分ける

一引数の指定は従来の共通adapterである。二引数の指定で、元のpropertyまたは構築関数のパラメーター型に応じたadapterを選べる。

```csharp
// 各adapterは利用側が実装する。Source<T>も利用側の値ソース型。
[ComposeArgumentAdapter(typeof(Arg<>))]
[ComposeArgumentAdapter(typeof(Source<>), typeof(SourceArg<>))]
[ComposeArgumentAdapter(typeof(string), typeof(TextArg))]
[ComposeArgumentAdapter(typeof(Source<string>), typeof(SourceTextArg))]
public class Component { }
```

選択順序は「宣言型との完全一致 → 宣言型のgeneric定義との一致 → 共通adapter」とする。設定は基底classからも読み、同じ対象に異なるadapterを重複指定した場合はLYC001とする。異なる対象への指定と共通指定は併用できる。対象型の変換可能性や基底class/interfaceへの変換による曖昧な一致は使わない。

| 宣言された型 | 生成引数型 | adapterが返す型 |
| --- | --- | --- |
| `int` | `Arg<int>` | `int` |
| `Source<int>` | `SourceArg<int>` | `Source<int>` |
| `string` | `TextArg` | `string` |
| `Source<string>` | `SourceTextArg` | `Source<string>` |

共通adapterはpublicな一引数の未構築generic structとし、宣言型全体で閉じる。generic対象への指定も一引数の未構築generic型同士とし、対象の型引数をadapterへ引き継ぐ。完全一致への指定では閉じたpublic structを使う。どの場合も、選択したadapterが`IComposeArgument<宣言された型>`を実装することを確認する。

例えば`SourceArg<int>`は`IComposeArgument<Source<int>>`を実装する。通常値からは定数sourceを作り、既存sourceからは同じ参照を保持する暗黙変換を利用側で定義できる。Generatorは宣言型を剥がしたり、購読・再構築を自動で選んだりしない。通常値への読み出しとsourceそのものの保持を、異なるadapterとして選べる。

### 補間文字列を直接受け取る

文字列用の`TextArg`と`SourceTextArg`は、利用側が`InterpolatedStringHandlerAttribute`を付けた通常のstructとして実装できる。Generatorはhandler型をそのまま引数型に使う。補間文字列の外側にfactoryや変換helperを要求しない。

```csharp
// using static Example.Compose;
// Scalar.Value: int、LiveScalar.Value: Source<int> として定義した場合。
Scalar(value: 10);
Scalar(value: source);
LiveScalar(value: 10);
LiveScalar(value: source);

// Label.Text: string、LiveLabel.Text: Source<string> として定義した場合。
Label(text: "固定テキスト");
Label(text: message);
Label(text: textSource);
Label(text: $"件数: {source:N0}件");
LiveLabel(text: "固定テキスト");
LiveLabel(text: textSource);
LiveLabel(text: $"件数: {source:N0}件");
```

この例のfactory・値ソース・adapterは利用側が定義する。`TextArg`は`IComposeArgument<string>`、`SourceTextArg`は`IComposeArgument<Source<string>>`を実装し、それぞれの`GetInitialValue`から宣言型どおりの値を返す。リテラルや通常のstring/sourceはadapterへの一段の暗黙変換で受け取り、補間文字列はC#のhandler変換で直接受け取る。

handlerは`(int literalLength, int formattedCount)`のconstructor、`AppendLiteral`、`AppendFormatted`を利用側で提供する。source要素を保持する場合はその型のoverloadを用意し、汎用の通常値overloadでオブジェクトのToStringへ変換しない。format/alignment/null/波括弧をどう評価し、いつ文字列を確定するかも利用側の契約とする。`ref struct`はrecipeへ保持できないためadapterとして指定できない。

生成factoryは引数型をstringやobjectへ戻さず、handler変換・暗黙変換はC#コンパイラーに任せる。未指定の`default(adapter)`と明示した空文字列・既定値は引き続き区別する。関数factoryも同じ選択規則を使い、元の定義関数には解決後の宣言型の値を渡す。

## 検討した代替案

- 特定の値ソース型をCompositionへ直接追加すると、すべての利用側がその実行基盤へ依存する。型付きadapter契約で分離する。
- 入力形式ごとの別factory群やscope付きinstanceは、同じ構築表記を維持しにくい。元のstatic入口へopt-inする。
- adapterをOptionalで包むとliteralから二段変換になり、通常の値引数が扱いづらい。三状態はadapter自身が保持する。

## 結果と影響

using static、子インデクサー、slot、withを維持して値引数を拡張できる。adapter側に値の具体化と受動的設定の責務が増えるが、購読や対象の寿命をCompositionへ持ち込まない。

## 検証方針

- adapter無しの既存consumerと生成結果が変わらず、opt-inした型と継承型だけが新しい引数になることを確認する。
- 実consumer compilationでliteral、adapter、required/default/null、nullable、delegate lambda、init-only、with、slot、別assemblyのcustom componentを検証する。
- GetInitialValue/constructor/with/AttachToの順序と失敗、未返却対象の外部購読なし、型付きsetter、cached memberKey、診断、AOT/trimmingを確認する。

## 実装記録

2026-10-30、元のUIスタックで契約3型とGeneratorのopt-in処理を実装し、UI非依存の契約・生成・テストをmain向け独立PRへ移した。既存consumerを含むCompositionテスト78件が通過した。UIを参照しない別consumerの継承adapter・init・delegate lambdaの実compilationと、不正adapterのLYC001を確認した。この記録は当初の共通adapter契約の検証である。2026-10-31、宣言型ごとの選択と、通常値・値ソース・補間文字列を直接渡す独立consumerの検証を追加した。
