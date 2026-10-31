# Lumyte.UI

Compositionで構築する保持型UIの基盤です。Panel、Stack、Button、背景スロット、親子関係、UiSurfaceの接続と一方向layoutを提供します。描画と入力は後続PRで追加します。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var content = Panel();
var background = Panel();
var button = Button()[content, Button.Background()[background]];
var root = Panel()[button];

using var surface = new UiSurface(root);
```

factory、子インデクサー、`Button.Background()`は既存Composition Generatorが生成します。通常の静的呼出しとして利用でき、構築用のcontextやruntimeは不要です。

- 子は通常内容・背景を通じて一つの親だけに属します。Buttonの通常内容は0〜1個、Panelと背景は複数を許可します。
- ChildrenとBackgroundChildrenは、基底Widgetが所有するsealedなWidgetChildrenです。getterだけを公開し、内容をそれぞれ一回だけ初期化できます。入力配列をコピーし、循環・重複・複数親・別slot・接続済みsurfaceの混入を変更前に拒否します。
- 同値・空を含む二回目の指定、追加・削除・並べ替えは拒否します。空の初期化も一回として数え、検証失敗は回数を消費しません。表示切替にはIsVisibleを使います。
- 失敗保証は列の初期化単位です。後の背景slotが失敗しても、その前の通常内容やslotの成功は残ります。同じ背景slotの二回目は拒否します。
- UiSurfaceとWidgetは作成threadへ固定しません。操作が重ならなければ、接続中も別threadで読取り・変更・Disposeできます。利用者が明示的に並行処理する場合の同期は利用者が管理します。ライブラリはlockや自動thread配送を追加しません。
- surface.Disposeは冪等で、背景を含む全接続を解除します。部分木の内部親子関係や外部資源を破棄しません。元のrootは新しいsurfaceへ再接続できます。

直接初期化する場合は`panel.Children.Initialize(child1, child2)`または配列を使います。任意のIReadOnlyListを渡すoverloadはありません。Compositionで生成するカスタムWidgetのComposeContentもWidgetChildrenかつget-onlyに限定され、違反はGeneratorのLYC001診断になります。単純なプロパティ代入でrootをたどるMutationScopeは使いません。プロパティ・初回内容設定・surface処理にphase検査は設けません。

低水準APIは最初の実装PRから`surface.Rebuild(Build())`で新しい宣言を予約し、`surface.UpdateTree()`で適用します。同じ親・子slot内で型/key（keyなしは型/位置）を照合し、componentの`OnReconcile`で管理する内部状態を引き継ぎます。任意fieldの自動コピーは行いません。layout導入後は`UpdateLayout()`がこの境界を自動実行します。通常の子コレクションは各宣言につき一回だけ初期化します。構築関数の登録・Signalによる自動再実行・表示要素の全差分再利用は後続実装です。

ライブラリはCompositionと、既存のRect2Dを使うGraphics.Drawing2Dを参照し、Generatorはビルド時のAnalyzerとして使用します。OS、GPU、Windowや入力pumpは所有しません。

## 一方向レイアウト

```csharp
using System.Numerics;
using Lumyte.UI;
using static Lumyte.UI.Compose;

var first = Panel(width: 80, height: 80);
var second = Panel(width: 60, height: 80);
var root = Stack(spacing: 8)[first, second];
using var surface = new UiSurface(root);
surface.SetViewport(new Vector2(200, 100));
surface.UpdateLayout();
// second.Boundsは(0, 88, 60, 80)。残り高さへ縮めない。
```

親は制約を渡し、子は制約内のサイズを返し、親は位置だけを決めます。Width/Heightは親制約内でtight化し、rootのサイズはviewportのtight制約を優先します。Stackは主軸をunboundedとして内容を一回ずつlayoutし、gapは可視の子同士の間だけに入れます。Buttonは内容から自身を決め、そのサイズで背景をlayoutします。

宣言プロパティのsetterはlayoutを無効化しません。値の変更は構築関数で新しいツリーを作ります。新しい構築結果は同じUiSurfaceへRebuildで予約し、次のUpdateLayoutで状態を引き継いで反映します。Signalによる自動の再構築は後続実装です。接続・子の初回初期化・viewport・表示側のruntime状態が変わるとdirtyになり、次のUpdateLayoutでrootから更新します。cleanなUpdateLayoutは再実行しません。Size/Boundsの読取はlayoutを実行せず、計算した値は各Widgetへ直接保存します。例外時は更新を中断し、先行ノードの変更を戻しません。ホストは描画と通常入力を停止し、破棄するか原因を修正して明示的に更新し直します。

カスタムの葉はWidgetのLayoutOverrideを、カスタムコンテナーはPanelのLayoutOverrideを拡張します。直接の可視子をLayoutChildで一回ずつ処理し、その返値を使ってSetChildOffsetで一回ずつ配置します。宣言値は構築時に設定し、setterからInvalidateLayoutを呼びません。表示側のruntime状態を変更する専用経路ではInvalidateLayoutを使えます。処理中の宣言を変更せず、同じsurfaceの処理を重ねない順序で呼び出します。phaseや再入の検出は行いません。別threadへの順次移行は接続中も利用できます。

ソースからのビルドには、既存Graphics.Drawing2Dが使うSlang環境を用意します。NuGetの利用者がlayoutを使うためにshaderを実行時コンパイルすることはありません。

[所有ツリーのADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)／[layoutのADR](../../../docs/adr/ui/UI-0003-layout-and-invalidation.md)

検証:

```sh
dotnet test tests/UI/Lumyte.UI.Tests -c Release
```

子コレクションの名前と役割は各コンポーネントが定義します。カスタムコンポーネントはconstructorでCreateChildrenを呼び、ForegroundやEmbeddeds等のget-onlyプロパティへ保持できます。Widget基盤は登録された全コレクションを所有権・接続・解除のために走査し、Background専用の保存領域は持ちません。配置・描画順は各コンポーネントの責務です。
