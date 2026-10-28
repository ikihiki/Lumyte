# Lumyte.UI

Compositionで構築する保持型UIの基盤です。現在はPanel、Button、背景スロット、親子関係とUiSurfaceの接続を提供します。layout・描画・入力は後続PRで追加します。

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
- 子リストのsetterは入力をコピーし、変更不能なsnapshotを返します。循環・重複・複数親・別slot・接続済みsurfaceの混入を変更前に拒否します。
- 同じ列の並べ替えは許可します。別親へ移す場合は先に元の列から外します。
- 失敗保証は列単位です。後の背景slotが失敗しても、その前の通常内容やslotの成功は残ります。
- UiSurfaceと接続中のWidgetは作成thread専用です。未接続treeは利用側で同期して別threadでも扱えます。
- surface.Disposeは冪等で、背景を含む全接続を解除します。部分木の内部親子関係や外部資源を破棄しません。元のrootは新しいsurfaceへ再接続できます。

ライブラリはCompositionだけを参照し、Generatorはビルド時のAnalyzerとして使用します。OS、GPU、Windowや入力pumpは所有しません。

[設計ADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)

検証:

```sh
dotnet test tests/UI/Lumyte.UI.Tests -c Release
```
