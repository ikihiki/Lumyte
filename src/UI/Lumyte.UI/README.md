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
- 子リストのsetterは入力をコピーし、変更不能なsnapshotを返します。循環・重複・複数親・別slot・接続済みsurfaceの混入を変更前に拒否します。
- 同じ列の並べ替えは許可します。別親へ移す場合は先に元の列から外します。
- 失敗保証は列単位です。後の背景slotが失敗しても、その前の通常内容やslotの成功は残ります。
- UiSurfaceとWidgetは作成threadへ固定しません。操作が重ならなければ、接続中も別threadで読取り・変更・Disposeできます。利用者が明示的に並行処理する場合の同期は利用者が管理します。ライブラリはlockや自動thread配送を追加しません。
- surface.Disposeは冪等で、背景を含む全接続を解除します。部分木の内部親子関係や外部資源を破棄しません。元のrootは新しいsurfaceへ再接続できます。

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

寸法・可視性・子の置換・viewportが変わるとdirtyになり、次のUpdateLayoutでrootから更新します。同値の指定やcleanなUpdateLayoutは再実行しません。Size/Boundsの読取はlayoutを実行せず、pass全体の成功後だけ新しい値を公開します。失敗後は前回値と再試行可能なdirty状態を保ちます。

カスタムの葉はWidgetのLayoutOverrideを、カスタムコンテナーはPanelのLayoutOverrideを拡張します。直接の可視子をLayoutChildで一回ずつ処理し、その返値を使ってSetChildOffsetで一回ずつ配置します。カスタム状態の変更はInvalidateLayoutをfield更新前に呼びます。layout中の値・子の変更や同じsurfaceの再入は拒否します。別threadへの順次移行は接続中も利用できます。

ソースからのビルドには、既存Graphics.Drawing2Dが使うSlang環境を用意します。NuGetの利用者がlayoutを使うためにshaderを実行時コンパイルすることはありません。

## 描画とクリップ

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;
using static Lumyte.UI.Compose;

var root = Stack(clipToBounds: true)
[
    Rectangle(width: 80, height: 80, fill: new Color2D(1, 0, 0, 0.5f)),
    Button()[Rectangle(width: 60, height: 80, fill: new Color2D(0, 0, 1, 1)),
        Button.Background()[Rectangle(fill: new Color2D(0.2f, 0.2f, 0.2f, 1))]]
];
using var surface = new UiSurface(root);
surface.SetViewport(new(200, 100));
var canvas = new Canvas2D();
canvas.Begin();
surface.Record(canvas); // 必要なlayoutを自動実行
DrawList2D list = canvas.End();
// listを既存Renderer2D.PrepareAsyncへ渡す。
```

Recordは毎回、可視ツリー全体を背景→自身→通常内容の順に記録します。Fillはfiniteな[0,1]のstraight-alpha sRGBで、既定は透明です。FillとClipToBoundsの変更はlayoutを無効化しません。viewportは常にclipされ、ClipToBoundsは自身と部分木を最終サイズの矩形へclipします。overflowした子のサイズは変更しません。

カスタムWidgetはOnRecord(DrawingCanvas2D)をoverrideし、原点0のローカル座標で記録します。親のclipは子の移動に追従せず、ホストのCanvas変換の内側へUIの原点を合成します。DPI変換はホストの責務です。OnRecordの変換・clipは内容や兄弟へ漏れません。Save/Restoreは釣り合わせ、借用中にBegin/End/Cancelを呼ばないでください。

描画中のツリー変更・layout/Record再入・Disposeは拒否します。例外時もCanvas状態を復元しますが、成功済み命令は残ります。frame全体をCancelしてからBeginで再試行してください。不均衡なSave/Restore等の契約違反による状態破壊は隔離しません。snapshotはsurfaceの変更・Dispose後も利用できますが、カスタム描画の借用GPU資源はGPU完了まで別途保持します。

RecordはCanvas、renderer、GPU、ClearやPresentを所有しません。単独UIでは各frameで描画先をClearし、重ね合わせではそのframeで再構成した背景へLoadします。複数surfaceのClearは最初に一度だけ行います。[直接描画サンプル](../../../samples/Lumyte.UI.Sample/README.md)に通常のCanvas→Renderer→Submit/Waitとviewport変更を示します。

[ツリーのADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)／[layoutのADR](../../../docs/adr/ui/UI-0003-layout-and-invalidation.md)／[描画のADR](../../../docs/adr/ui/UI-0004-drawing-and-clipping.md)

検証:

```sh
dotnet test tests/UI/Lumyte.UI.Tests -c Release
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release
```

命令snapshotの統合テストはDrawing2D.Testsに配置しています。実GPUの画素・連続frameの検証はGraphics.TestsのUiDrawingTestsを使い、実行環境で`LUMYTE_GRAPHICS_GPU_TESTS=1`を設定します。
