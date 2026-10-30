# ADR-COMPOSITION-0004: 所有コレクションへの内容初期化と継承可能な型制約

- 状態: 採用
- 日付: 2026-10-28

## 背景

[COMPOSITION-0001](COMPOSITION-0001-declarative-composition.md)の子インデクサーは配列を対象メンバーへ代入する。UIでは親に結び付いた専用コレクションを保持し、その内容を一度だけ設定したい。読み取り専用プロパティを通じて初期化し、Widget派生型が別のコレクション型でこの契約を回避することも生成時に検出する必要がある。

## 決定

既存の配列・標準collection interfaceへの代入を維持し、get-onlyな所有コレクションへの初期化を追加する。比較元はmain `4773a2b73a3a9a1a2b1d0b5e6c99364eaf070150`とする。生成契約・テストはmain向けComposition拡張PRへ独立させ、具体的な所有コレクションは利用側で実装する。CompositionはUIの型・所有ツリー・一回限りという具体的制約へ依存しない。

```diff
+namespace Lumyte.Composition
+{
+    public interface ICompositionContent<T>
+    {
+        // 生成インデクサーが通常内容を配列で渡す。保持する実装は入力をコピーする。
+        // 個数・所有関係・初期化回数・失敗保証は実装が定義する。
+        void Initialize(T[] content);
+    }
+    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
+    public sealed class CompositionContentTypeAttribute<TContent> : Attribute
+    {
+        // 型引数を制約とし、属性は生成対象と全基底型で確認する。
+        public CompositionContentTypeAttribute();
+        public Type ContentType { get; }
+    }
+}
```

`ComposeContent`のメンバー型が`ICompositionContent<T>`を実装する場合、setterの代わりに、そのメンバーをinterfaceへcastしてInitializeを一度呼ぶ。所有ストレージは参照型に限定する。struct、および参照型の制約がない型引数は、boxingによるコピーの初期化を防ぐためLYC001で拒否する。この検査はCompositionContentType属性の有無にかかわらず適用する。プロパティにはアクセス可能なgetterが必要であり、init-only setterは引き続き非対応とする。配列の作成・子の評価は通常のC#の呼出し規則に従う。

`[CompositionContentType<OwnedChildren>]`を指定したクラスと派生クラスでは、ComposeContentの型を指定型と完全一致させ、プロパティのsetterとreadonlyでないfieldを禁止する。型は属性の型引数で指定し、`ContentType`は`typeof(TContent)`を返す。型引数に追加の基底型・interface制約は設けない。閉じたgenericコレクションも指定でき、構築済みの型全体を比較する。全ての基底型の制約を同時に満たす必要があり、派生型の属性で制約を緩和できない。違反は既存のLYC001診断として報告する。生成対象外の通常のC#メンバーや、業務データ用のコレクション宣言を禁止する機能ではない。

通常内容とslotの混在では、通常内容をまとめてInitializeし、その後slotを記述順に適用する。slotだけの指定では通常内容を初期化しない。空配列を通常内容として渡した場合はInitializeへ空配列を渡す。slotメソッドの引数は従来どおり配列・対応collection interfaceであり、所有コレクションそのものを引数にしない。

初期化時の例外は呼出し元へ伝播する。式全体のrollbackは追加しない。後のslotが失敗しても、先行した内容・slotの成功は残る。生成されたインデクサーは同じ対象instanceを返す。

## 検討した代替案

- CompositionへWidgetChildrenを直接組み込むとUI非依存性を失う。
- 配列から専用コレクションへの暗黙変換では、その生成時点で親が分からず、所有ストレージの置換を許してしまう。
- `typeof`をコンストラクター引数へ渡す属性より、generic属性で要求型を直接指定するAPIを採用する。未マージのAPIを置換し、非generic属性は公開しない。
- UI専用Analyzerで同じ制約を実装すると、生成側の対応と診断が別々になる。今回は汎用の型制約をGeneratorが検査する。

## 結果と影響

既存の構築構文を保ち、get-onlyな専用ストレージへ内容を渡せる。派生型に適用する型制約もUIに依存せず検査できる。独自コレクション実装の回数制限や親子関係の検証は、Compositionが肩代わりしない。

## 検証方針

既存の代入型contentとslotのテストを維持し、所有コレクションのgetter-only生成、継承したindexer、slot混在、異なる型・setter付き宣言の拒否、派生属性による制約緩和の拒否、閉じたgeneric型の完全一致、属性の完全修飾名・aliasの解決を確認する。UIの実際の型を別assemblyから構築する検証も行う。

継承先で通常内容とslotを混在させる場合、生成するCompositionChildが基底型の同名型を隠すことをnewで明示する。同じ生成処理内の基底型と、別assemblyで生成済みの基底型の両方を扱い、派生componentのslot追加でCS0108を発生させない。
