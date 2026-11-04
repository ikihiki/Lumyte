# ADR-UI-0002: Widget宣言とElementツリーの所有権・Composition構築

- 状態: 採用・実装済み（PR 1）
- 日付: 2026-10-27
- 対応: 実装PR 1（#77）
- 更新: 2026-10-31

## 背景

Compositionで生成するWidgetは毎回新しく構築できる宣言とし、状態と寿命を持つ実体を
Elementへ分離する。本ADRを分離基盤の共通契約とする。
この分離は最初のUI実装から提供し、SignalやTextBoxまで状態保持の基盤を延期しない。

## 決定

### 公開APIと保存領域

```csharp
public sealed class UiSurface : IDisposable
{
    public UiSurface(Compose.Definitions.Widget root);
    public Compose.Definitions.Widget Root { get; }
    public UiElement RootElement { get; }
    public void Rebuild(Compose.Definitions.Widget root);
    public void UpdateTree();
    public void Dispose();
}
```

Widgetは値、callback、key、子宣言、再生成情報を持つ。Elementは現在のWidget、
Surface、Parent、slotごとの子、任意の部品状態を持つ。
型付き`UiElement<TWidget>`、CreateElement、PrepareUpdateで独自部品を実装する。
WidgetにmountedなParent / Surface / Elementや幾何の保存先を置かない。
状態を新しいWidgetへ移すOnReconcileは廃止する。

UiSurfaceはElementツリーを所有する。Rootは現在採用したroot設定、RootElementはその実体。
Rebuildは新しいroot宣言を予約し、UpdateTreeが照合・検証後に採用する。UpdateTree単独では
layoutを実行しない。破棄済みSurfaceの利用はObjectDisposedException、Disposeは冪等とする。

### コンポーネントごとの子プロパティ

`WidgetChildren`はsealedな宣言専用の`IReadOnlyList<Widget>`および
`ICompositionContent<Widget>`とし、初回成功のInitializeだけを公開する。
IList、setter、Add / Remove / Clear、任意のIEnumerableを受け取るoverloadは設けない。
Compositionからは配列を受け取り、利用者が列挙結果を渡す場合は明示的にToArrayする。

WidgetはconstructorのCreateChildrenでslotを登録し、Children / BackgroundChildren /
Foreground / Embeddeds等のget-onlyプロパティに保持する。名前と役割は基盤が解釈しない。
Panelは通常内容、Buttonは容量1の通常内容と複数の背景を登録する。
子slotの型は`[CompositionContentType<WidgetChildren>]`で固定し、異なる型・setter付きの
ComposeContentはCompositionのLYC001で拒否する。汎用生成契約はmainの
[COMPOSITION-0004](../composition/COMPOSITION-0004-owned-content-collections.md)を使う。

実行時は各slotをElement所有のUiElementChildrenとして保持する。
同一slotの子宣言が変わっても、保持する子Elementやslot viewを不要に作り直さない。
layout / input / recordはこの実行時slotを使う。WidgetChildrenをmountedなlive viewに変換せず、
宣言と実行時ツリーを二重に書き換えない。

### 初期化と所有権

初回のInitializeは入力配列をコピーし、個数、null、同じ親内の参照重複と循環を
検証してから内容を設定する。失敗では内容と初期化回数を変更しない。
空配列でも成功したら一回を消費し、同値・空・並べ替えを含む再設定を拒否する。
未初期化の読取りは空。slotごとに一回なので、通常内容と背景は独立して初期化できる。
後から適用するslotが失敗しても、既に成功した別slotの内容は維持する。

単一親・単一Surface・非循環の実行時所有権はElementへ適用する。
Widget宣言は別の親・Surfaceから再利用でき、宣言へ親を保持しない。
各collectionのOwnerと、表示実体のParentは別の契約である。
同じroot宣言を独立したSurfaceへ渡しても、Elementや内部状態は共有しない。
共有するモデルSignalの値と、各表示実体の状態は別の寿命になる。
animation producer等が持つ既存の所属制約は引き続き検証する。

Initializeは構築用であり、mount後の初回Initializeから表示ツリーを直接拡張するAPIは廃止する。
公開済み宣言のsetterと同様に、表示更新には新しい宣言を構築して渡す。
このためのphase検査や利用者向けMutationScopeは追加しない。
任意の外部listのCount / indexer / callbackを所有関係の更新中に呼ばない。

### スレッド・再入・ライフサイクル

Widget / Element / UiSurfaceは作成threadへ固定しない。読取りを含む明示的な並行アクセスの
同期は利用者が管理し、通常hostは更新・入力・layout・記録の順序を自動管理する。
構築済み宣言を採用処理や描画と並行して書き換える利用はサポートしない。

CreateElementとconstructorはpassiveな状態だけを準備する。OnMountedでリソースを開始し、
同じidentityの引数変更では現在設定を交換してOnWidgetUpdatedを呼ぶ。
OnUnmountedで論理的な寿命を終了し、購読等を解除する。
入力取消は状態を参照できる間に行い、残りの要素の清掃も試みる。
最終提示、GPU退役、DI解放は各統合層の契約に従い、論理unmountと同一視しない。

DisposeはElementを終了するが宣言の内容を消費しない。同じ宣言を後で接続する場合も
新しい実体を作り、内部状態は初期化する。一回初期化済みの子列はそのまま使用する。

### 構築関数の再実行による動的な子の変更

```csharp
using Widget = Lumyte.UI.Compose.Definitions.Widget;
using static Lumyte.UI.Compose;

Widget Build(bool showDetails) => Panel()[
    showDetails ? Panel()[Rectangle(width: 80, height: 40)] : Panel()];
```

動的な追加・削除・並べ替えは構築関数の再実行で新しい宣言を作る。
一回だけ設定する対象は各WidgetChildrenであり、構築関数の呼出し回数ではない。
状態変化ごとのWidget生成を許容し、利用者へ差分更新用の変更可能collectionを要求しない。

照合は同じ親Element・同じslot内に限定する。keyありは型/key、keyなしは型/位置を使う。
key空間はslotごとに分け、重複keyは公開前に拒否する。
同じElementでは通常フィールドを保持する。型、key、親、slotが変わった場合と除去後の
再追加では新しい寿命になる。内部状態を別の場所へ自動移管しない。

## 全Widget共通の交換基盤（PR #77）

再生成・照合・投影検証・準備・公開を全Widgetに共通化する。
既存Elementのruntime slotと新Widgetを照合し、公開予定の設定と子Element列をplanへ保持する。
旧新Widgetの両方へmountedツリーを作ったり、公開後にWidget参照を修復したりしない。
任意の関数、callback、通常fieldを自動的に状態へ変換する機能は提供しない。

### 手書きWidgetの拡張点

CreateElementで型付きElementを作り、独自状態とlayout / input / recordの実装を置く。
WidgetのRebuildDeclarationは受動的な再生成情報から新しい宣言を返す。
既定は生成factory。再生成手段のない部品は既存のSurface構築関数へ委譲できる。
画面側へcontextやruntimeを渡させない。

UiWidgetRebuildContextは子保持をplanへ記録する。保持は旧WidgetChildrenの内容を
新しいcollectionへ移管する操作ではなく、既存runtime slotを継続する指示とする。
引数だけの局所更新では未指定slotを維持し、新しく初期化したslotはその宣言を使う。
全体Rebuildや関数bodyの空slotは空として扱う。

通常の子設定変更でElement参照は変わらないため、子Widget fieldの交換hookは廃止する。
独自の子索引や選択先はElementのPrepareUpdateで更新後の子列を確認し、
返すActionで公開後に変更できる。親の宣言が変わらない子identity / 順序変更にも適用する。
PrepareUpdateは新しい設定と更新予定の子Elementを受け取り、公開時のActionを返す。

### 公開前の全体検証

投影ビューは、現在のElementに公開予定の設定・親子・新規実体を適用した読み取り専用view。
validatorは予定設定を参照し、Widget.Parentやliveな子列を使わない。
全体参照のvalidatorは別の部分木の変更でも再評価するが、無関係な宣言は再生成しない。
Popupのanchor、共有keyなどもこのビューを利用する。

未公開の準備失敗は現行Elementと購読を維持する。
公開後のcustom Action / lifecycle / layout例外では描画と通常入力を停止し、rollbackしない。
通知順と候補資源の所有は以下の公開手順に従う。

## 実装PRへの配置と検証

PR #77にWidget / Element、runtime slot、照合、状態保持、lifecycle、投影検証と
手書き部品の拡張を含める。#78以降は同じElementへ機能を追加し、#93で自動購読、
PR #97で関数bodyとUseを接続する。CompositionとSignalsの独立部分を混ぜない。

一回初期化、slot、循環・重複・宣言共有と実体の分離、型/key/位置、深いツリー、
独自フィールドの継続、子交換、候補失敗、callback例外、取消と破棄、
再接続時の新実体、同じ宣言の独立mountを検証する。
後続機能ではWidgetにruntime状態が残っていないことも確認する。

## 過去の実装・検証記録

2026-10-29の#77では、Widget中心の所有ツリー、OnReconcileによる状態転送、接続中の
子初回初期化を実装し、状態保持9件を含むUI 29件とComposition 61件が成功した。
その後の実験ではElementを追加して状態・購読を移し、UI関連579件とNativeAOTを検証した。
これらは当時のrevisionの記録であり、上記再設計の移行後検証を意味しない。

## Element基盤の公開手順

同一性は同じ親Element・同じslot内の型/key、keyなしは型/位置で決める。
関数は生成された関数種別を型識別に含める。異なる親・slot・Surfaceへ状態を移さない。
同一slot内の重複keyは公開前に拒否する。共有アニメーションkeyは別の名前空間とする。

1. dirtyなElementを集約し、子を再構築する親に包含される要求をまとめる。
2. 対象の再生成情報から新しいWidgetを構築する。明示RebuildとSurface構築関数の
   直接依存だけは全体を構築する。通常の引数変更はそのElementの宣言を再生成する。
3. 現在のElement slotと新しい子宣言を照合し、更新予定の設定・子・除去集合を作る。
   同じslotの保持はplan上の指定とし、旧WidgetChildrenを新しい宣言へ移管しない。
   明示的な保持指定も同じowner・slotの対応に限定し、別ownerやslotへ状態を移管しない。
   初期化済みの新しいslotはその宣言を優先し、初期化しないslotは引数だけの局所更新で保持する。
   全体Rebuildと関数bodyで生成した空slotは空として採用する。
4. 投影ツリーで検証し、ElementのPrepareUpdate、無効化差分、購読変更を準備する。
   validatorはUiWidgetValidationContextを通して、予定Elementに対応する設定・親子を読む。
   Nodes / GetChildren / GetParentは予定Widget設定のviewを返し、Widget.Parentやlive fieldを辿らない。
5. 採用するWidgetとElement slotを公開し、購読のrendered値・Use tableを確定する。
   この内部反映では利用者callbackを実行しない。
6. prepareのActionを実行し、除去・入力不適格化のcapture / focus / IMEを取り消す。
   その後に除去実体のOnUnmounted、新規のOnMounted、維持実体の更新通知を行う。
   Actionや通知の例外でも取消・終了対象の残りの清掃を試み、新しいリソースの開始は止める。
7. 必要なlayout / input情報を更新し、その後に描画を記録する。

未公開の準備失敗では現在のElement・状態・購読を変更せず、候補所有の資源だけを解放する。
公開後のcallbackやlayoutの失敗は描画と通常入力を停止し、更新済みfieldを巻き戻さない。
以前の画面へ戻すための幾何snapshotは持たない。

RootElementは継続する実体、Rootは現在のroot宣言を示す。
取り外したElementを再mountせず、同じ宣言を新しいSurfaceへ接続した場合も新しい実体を作る。
同じroot宣言を独立したSurfaceへ接続してもElementや内部状態を共有しない。
animation producer等の借用sourceに既存の所属制約がある場合は、その制約も維持する。
宣言の一回初期化は再接続でもリセットしないが、押下・編集等の内部状態は復元しない。
再追加後も維持したい業務状態は外部Signalへ置く。

## 型付きElementの拡張API

```csharp
public class UiElement<TWidget> : UiElement
    where TWidget : Compose.Definitions.Widget
{
    protected UiElement(TWidget widget);
    public new TWidget Widget { get; }
    protected virtual Action? PrepareUpdate(TWidget next, UiElementUpdateContext context);
    protected virtual void OnWidgetUpdated(TWidget previous);
}
```

Widget.CreateElementの既定は空のUiElement。状態を持つ部品はoverrideして派生Elementを返す。
通常のfieldは同じ実体に残り、設定からElementを検索・生成するgetterは公開しない。
構築用のWidgetChildrenと実行時のUiElementChildrenは別の型で、slot viewは同じownerで継続する。
PrepareUpdateは新規実体にも呼び、候補の子参照を用意できる。返すActionは全体の設定・slotの
公開後に実行し、OnMountedはその後に呼ぶ。例外では除去実体の残りの清掃も試みる。

```csharp
protected override UiElement CreateElement() => new CounterElement(this);

private sealed class CounterElement(Counter widget) : UiElement<Counter>(widget)
{
    internal int Count;
}
```

layout・入力・描画のhookと無効化は各担当PRでElementへ追加する。
基盤単独ではComposition以外の実行時依存を追加しない。

## Element分離の実装・検証記録

2026-10-31、#77単独でWidgetとElement、型付き状態、runtime slot、共通の部分木公開を実装した。
従来のOnReconcile・子Widget参照交換・Widgetのmounted親／Surfaceを廃止した。
Release UIテスト47件が成功。型/key/slot、20,000段、独自の子選択、投影validator、
未初期化slotの局所保持、準備失敗、再入予約、mount失敗・通知中の破棄と清掃を確認した。
この段階はlayout・入力・Signalをまだ含まず、各機能の検証は担当PRで行う。

Widget.Childrenは元の宣言内容であり、部分更新後の実行中の子一覧ではない。
現在の子と設定を参照する診断・独自hostはRootElement.Childrenと各Element.Widgetを使用する。
未初期化slotの保持は部分更新対象の宣言に限り、新しく構築した子ツリーの空slotへ古い子を戻さない。

## レビュー後の識別子・共有・検証の契約

Keyはinit-onlyの構築値とし、Signal/Computed用の引数adapterを適用しない。
関数factoryのkeyもstringの構築値である。動的なリストは安定したデータIDをkeyにし、
構築関数で新しい宣言を返す。明示的にkeyが変われば新しいElement寿命となる。

Computedが保持した子配列を、新しい親の一回初期化へ再利用できる。
子のWidgetへ親参照を記録せず、実体は親・slot・型・keyでそれぞれ照合する。
同じ親のslot群内での同一宣言の重複、同一slotの重複key、循環は引き続き拒否する。
共有宣言のvalidatorは自身のcontextからGetParent(this)/GetChildren(this)を呼ぶと、
そのElementに対応する予定トポロジーを参照する。別branchに重複する宣言の曖昧な参照は拒否する。

slot数の変更自体をトポロジー変更とし、0slotや空slotへの縮小でもChildrenを更新する。
継続するslot viewは維持し、取り外すslot viewは空にする。取り外したElementを公開Childrenに残さない。

借用サービスの検証が必要なElementを共通indexへ登録する。RequiresHostValidationと
HostServicesChangedで対象と要件差分を宣言し、HostValidationRevisionは登録・除去・要件変更時だけ進める。
色など無関係な設定変更ではこのrevisionを進めない。具体的なサービス検査は各hostへ置く。

Hostサービス検証の拡張hookは、公開済み設定と購読の採用後、除去Elementを必ず清掃する
try/finally内で実行する。hook例外時は公開を巻き戻さず通知・描画を中断し、
除去Elementのunmountと入力取消を完了してから例外を返す。
