# Lumyte.UI

Compositionで構築する保持型UIの基盤です。Panel、Button、背景スロット、親子関係、UiSurfaceの接続と寿命を提供します。layout・描画・入力は後続PRで追加します。

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
- 同値・空を含む二回目の指定、追加・削除・並べ替えは拒否します。空の初期化も一回として数え、検証失敗は回数を消費しません。可視性とlayoutは後続PRで追加します。
- 失敗保証は列の初期化単位です。後の背景slotが失敗しても、その前の通常内容やslotの成功は残ります。同じ背景slotの二回目は拒否します。
- UiSurfaceとWidgetは作成threadへ固定しません。操作が重ならなければ、接続中も別threadで読取り・変更・Disposeできます。利用者が明示的に並行処理する場合の同期は利用者が管理します。ライブラリはlockや自動thread配送を追加しません。
- surface.Disposeは冪等で、背景を含む全接続を解除します。部分木の内部親子関係や外部資源を破棄しません。元のrootは新しいsurfaceへ再接続できます。

直接初期化する場合は`panel.Children.Initialize(child1, child2)`または配列を使います。任意のIReadOnlyListを渡すoverloadはありません。Compositionで生成するカスタムWidgetのComposeContentもWidgetChildrenかつget-onlyに限定され、違反はGeneratorのLYC001診断になります。単純なプロパティ代入でrootをたどるMutationScopeは使いません。プロパティ・初回内容設定・surface処理にphase検査は設けません。

低水準APIは最初の実装PRから`surface.Rebuild(Build())`で新しい宣言を予約し、`surface.UpdateTree()`で適用します。同じ親・子slot内で型/key（keyなしは型/位置）を照合し、componentの`OnReconcile`で管理する内部状態を引き継ぎます。任意fieldの自動コピーは行いません。layout導入後は`UpdateLayout()`がこの境界を自動実行します。通常の子コレクションは各宣言につき一回だけ初期化します。構築関数の登録・Signalによる自動再実行・表示要素の全差分再利用は後続実装です。

ライブラリはCompositionを参照し、Generatorはビルド時のAnalyzerとして使用します。OS、GPU、Windowや入力pumpは所有しません。

[所有ツリーのADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)／[所有コレクションの生成契約](../../../docs/adr/composition/COMPOSITION-0004-owned-content-collections.md)

検証:

```sh
dotnet test tests/UI/Lumyte.UI.Tests -c Release
dotnet test tests/Core/Lumyte.Composition.Tests -c Release
```

子コレクションの名前と役割は各コンポーネントが定義します。カスタムコンポーネントはconstructorでCreateChildrenを呼び、ForegroundやEmbeddeds等のget-onlyプロパティへ保持できます。Widget基盤は登録された全コレクションを所有権・接続・解除のために走査し、Background専用の保存領域は持ちません。配置・描画順は各コンポーネントの責務です。
