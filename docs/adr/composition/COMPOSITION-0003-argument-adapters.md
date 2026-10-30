# ADR-COMPOSITION-0003: 値引数adapterの生成時指定

- 状態: 採用・実装済み（main向けComposition拡張PR）
- 日付: 2026-10-30
- 対応: main向けComposition拡張PR、前提: COMPOSITION-0001・COMPOSITION-0002

## 背景

UIの保持型Widgetでは、名前付き引数へ通常値とSignalを同じ表記で渡したい。現行factoryはOptionalで具体値を受け、値を一回設定する。UI側に別factoryや明示Bindを要求せず、元のusing static構文を維持するため、生成引数を型付きadapterへ委譲するopt-in契約を追加する。

CompositionはUIやSignalsの型を参照せず、adapterの値解決と対象への付加情報登録だけを呼び出す。購読、描画、親子関係や対象の寿命は利用側の責務とする。

## 決定

### 配置と公開API

既存Lumyte.CompositionとComposition.Generatorsを拡張する。契約はBCLのみ、Generatorは既存と同じRoslyn基準を使う。比較元はmain `4773a2b73a3a9a1a2b1d0b5e6c99364eaf070150`と[COMPOSITION-0002](COMPOSITION-0002-callback-parameters.md)のAPIである。

```diff
+namespace Lumyte.Composition
+{
+    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
+    public sealed class ComposeArgumentAdapterAttribute : Attribute
+    {
+        // publicなgeneric structの未構築型。閉じる型引数は対象propertyの型。
+        public ComposeArgumentAdapterAttribute(Type adapterType);
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

attributeは定義classまたは基底classへ付ける。Generatorが継承階層を走査して採用するadapterを決定し、矛盾する複数指定はLYC001 errorとする。adapter指定のないcomponentでは現行生成APIを変えない。具体的なadapterの実装・購読・寿命は利用側が提供する。CompositionにUIやSignalsの参照を追加しない。

### 引数の生成規則

変更可能な非delegateのComposeParameterだけを対象にする。adapterをpropertyの型Tで閉じ、optional引数はそのstructのdefault、requiredな変更可能値は省略不能なadapter引数とする。Optionalを外側に重ねず、二段のuser-defined conversionを要求しない。

init-only値は後から変更できないためadapterを使わず、元の値型/Optional規則を維持する。delegate型は[COMPOSITION-0002](COMPOSITION-0002-callback-parameters.md)に従う元のdelegate引数とし、lambdaをstructへ変換させない。content、with、操作、slotは従来の型と表記を維持する。

元のfactoryにadapter版のoverloadを追加せず、opt-inしたcomponentの専用delegateの引数型を置き換える。UIは提案段階でのAPI拡張として扱う。明示的にOptional型を指定して呼んでいたコードは移行対象であり、全consumerの無変更互換を宣言しない。

### 生成処理と失敗

1. required adapterのIsSpecifiedを全件確認する。未指定はWidget等を作る前にArgumentExceptionとする。
2. 指定済みadapterのGetInitialValueを生成引数順に一回ずつ呼び、初期値をlocalへ保持する。失敗では対象を生成しない。
3. 解決した具体値を使い、従来の生成constructorでrequired/initと通常parameterを設定する。withは従来どおりその後に実行する。
4. 全て成功したら指定済みadapterのAttachToを引数順に呼び、元の対象instanceを返す。memberKeyは生成memberごとのcached object、setterは定義型内に生成した型付きstatic helperを使う。アクセス可能性とsetterの制約を引き継ぐ。

AttachToは対象自身が所有する受動的な設定情報だけを登録し、外部購読、thread起動、UI接続、global registryへの登録を開始しない。後続AttachToで失敗しても外部から対象を保持する資源がなく、未返却対象はGC可能となる。constructor/withの外部副作用や、利用者がそこから公開した対象への変更をrollbackしたとは扱わない。withの後も初期値localを渡し、withが上書きした値との優先順位はadapter側で定義する。

再構築を必要とする対象は追加の`ICompositionRebuildable.SetCompositionFactory(Func<object> factory)`を実装する。adapter指定のfactoryはAttachTo成功後に、元の引数とwithから新しい同型宣言を構築する受動的recipeを一回登録する。recipeを実行するのは利用側であり、Generatorは購読を開始しない。init、callback、withも再実行され、withの優先順位を保つ。子のindexerはfactoryの後なのでrecipeには含めず、利用側が登録済みslot構造と子recipeを組み合わせる。手書きnewを含む動的ツリーの再構築方法も利用側が定義する。

publicな通常のgeneric struct一引数でない型（ref structや未構築のgeneric外側型を持つadapterを含む）、閉じた型がIComposeArgumentを実装しない場合、型制約不一致、非対応member、アクセス不能、生成名衝突は既存LYC001のerrorで拒否する。adapterのconstructorやGetInitialValueをGenerator自身が実行して検証しない。実行時reflection、DynamicInvoke、新しいruntimeの作成は行わない。

## 検討した代替案

- CompositionへSignal型を直接追加するとUI以外の構築基盤もSignalsへ依存する。genericなadapter契約で分離する。
- Signal専用の別factory群やscope付きinstanceは、同じ構築表記を維持しにくい。元のstatic入口へopt-inする。
- adapterをOptionalで包むとliteralから二段変換になり、通常の値引数が扱いづらい。三状態はadapter自身が保持する。

## 結果と影響

using static、子インデクサー、slot、withを維持して値引数を拡張できる。adapter側に値の具体化と受動的設定の責務が増えるが、購読や対象の寿命をCompositionへ持ち込まない。

## 検証方針

- adapter無しの既存consumerと生成結果が変わらず、opt-inした型と継承型だけが新しい引数になることを確認する。
- 実consumer compilationでliteral、adapter、required/default/null、nullable、delegate lambda、init-only、with、slot、別assemblyのcustom componentを検証する。
- GetInitialValue/constructor/with/AttachToの順序と失敗、未返却対象の外部購読なし、型付きsetter、cached memberKey、診断、AOT/trimmingを確認する。

## 実装記録

2026-10-30、元のUIスタックで契約3型とGeneratorのopt-in処理を実装し、UI非依存の契約・生成・テストをmain向け独立PRへ移した。既存consumerを含むCompositionテスト78件が通過した。UIを参照しない別consumerの継承adapter・init・delegate lambdaの実compilationと、不正adapterのLYC001を確認した。UIの実consumerではliteral/Signal/Computed、with、背景slot、再構築で旧宣言を変更しないことを確認し、通常のHeadless/Browserとlinux-x64 Native AOTで生成factoryを実行した。
