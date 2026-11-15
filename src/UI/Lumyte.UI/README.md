# Lumyte.UI

Compositionの静的factoryでWidget宣言を作り、UiSurfaceが継続するElementを管理する。
最初のPRは所有権、型/key/slotの照合、状態保持、公開と破棄を提供する。
レイアウト・描画・入力は後続PRで同じElementへ追加する。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

using var surface = new UiSurface(Panel()[Button(key: "action")]);
UiElement before = surface.RootElement.Children[0];
surface.Rebuild(Panel()[Button(key: "action")]);
surface.UpdateTree();
// beforeと現在の子は同じ実体。Widget設定だけが新しくなる。
System.Diagnostics.Debug.Assert(ReferenceEquals(before, surface.RootElement.Children[0]));
```

通常の子と名前付きslotはそれぞれ一回だけ初期化できる。動的な内容は新しく構築する。
WidgetChildrenは構築時の宣言、UiElementChildrenは実行時の子を示す。
Widgetにmounted親、Surface、Element、幾何や状態転送のhookを持たせない。

独自部品はWidgetへ設定を置き、CreateElementから型付きUiElementを返す。
Elementのfieldは再構築で維持され、OnReconcileによるコピーは不要。
PrepareUpdateで更新後の子を参照し、公開後に独自の索引や選択先を変更できる。
OnMountedでリソースを開始し、OnUnmountedで解放する。constructorはpassiveなデータだけを用意する。

keyは同じ親・同じslot内で有効で、keyなしは型/位置を使う。
型・key・親・slotの変更と除去後の再追加は新しい寿命になる。
同じ宣言から独立したSurfaceを作っても状態は共有しない。Signal等の借用sourceの制約は別途従う。

準備失敗では現在の状態を保持する。公開後の利用者callbackの例外では更新を停止し、rollbackしない。
Disposeは冪等で、Elementの寿命を終了して宣言を残す。再接続では新しいElementを作る。
CPUの作成threadへ固定せず、明示的な並行操作は利用者が同期する。

[所有権・状態・公開順のADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)

## レイアウト

SetViewportで論理サイズを渡し、UpdateLayoutで更新する。
RootElementとruntime slotのSize / Boundsから配置を参照する。Widgetには配置getterを持たせない。
子へ制約を渡し、確定サイズを受け取り、位置だけを指定するone-pass方式を使う。

```csharp
surface.SetViewport(new(800, 600));
surface.UpdateLayout();
System.Numerics.Vector2 size = surface.RootElement.Size;
```

独自コンテナーは型付きElementのLayoutOverrideでGetChildren(Widget.Children)を使う。
LayoutChildとSetChildOffsetは直接の子Elementを受け取り、各可視子に一回ずつ適用する。
設定差分はCompareConfigurationからUiInvalidationへ分類し、共通の寸法差分は基盤で合成する。
通常の宣言setterはdirtyを伝播しない。

## 描画

Begin済みCanvasへsurface.Record(canvas)を呼ぶ。Clear・GPU投入・提示はhostが担当する。
ElementのOnRecord / RecordOverride / RecordChildで描画し、独自の子描画順はruntime slotから定義する。
RectangleのFillはWidgetの設定、Size / BoundsはElementの幾何を使う。色は`SRgba<float>`で保持し、UIへの受入時に全成分の[0,1]を検証する。`LRgba<float>` は描画色へ暗黙変換しない。animation projection の宣言型が `SRgba<float>` またはその nullable の場合だけ unit 検証し、linear/HDR や object の一般データにはこの制約を課さない。
成功した描画命令はWidget / Elementを遅延参照せず、後の再構築とは独立する。

## ポインター入力

通常の値と同じ静的factoryのonClick引数へcallbackを渡す。
UpdateLayout成功後にUiPointerInputをDispatchする。Dispatchは配置の準備を行わない。
UiInputContext.ReceiverはElementであり、独自部品はElement.OnInputで処理する。
capture・押下状態は同じ実体に残り、宣言交換だけで作り直さない。
型/key変更・除去・無効化やPrepareUpdateで要求した取消は、実体の清掃前に通知する。

## Focusとキー

Focusableは宣言の設定、focus対象と押下はElementの状態として保持する。
通常hostはUiKeyInputをDispatchし、Tabと各コントロールのキー認識を共通経路で処理する。
低水準の明示操作にはFocusedElementとTryFocus(UiElement?)を使う。
宣言の再生成ではfocusやkey captureを付け替えず、除去・不適格化・明示取消だけを処理する。

## 実Windowとの接続

[UI-0007](../../../docs/adr/ui/UI-0007-window-input-and-dpi-integration.md)では、取得Window情報付きのInputRecordを
UiInputへ変換し、native / hostedの入力系列をhostで所有する。DPIはCanvasへ一度だけ適用する。
UI本体のPlatform / Windowing / InputSystem非依存を維持し、操作対象はSurface内のElementとする。

## RenderGraphと資源寿命

記録した命令を既存のRenderGraph / Resourcesへ接続する。Elementの論理寿命とGPU退役は区別する。
[UI-0008](../../../docs/adr/ui/UI-0008-render-graph-and-resource-lifetime.md)のticketとleaseの契約を使い、
後から変わるElementやWidgetの設定をGPU処理が遅延参照しない。

## 自動操作

testId / automationName / roleはWidgetの設定、操作IDとpressed / focusはElementの状態とする。
CaptureAutomationSnapshotは現在のElementを値へコピーし、通常Dispatchでは取得しない。
同じ型/key/slotの再構築ではIDを保ち、別のSurfaceや除去後の再追加では別IDになる。
独自部品のElementでGetAutomationChildrenとOnProbePointerDownを実装できる。
[Automation](../Lumyte.UI.Automation/README.md)のLocatorは検索条件を保持して毎回現在の実体を探す。

## Signal引数と部分木更新

```csharp
var width = Lumyte.Signals.Signal.Create<float?>(20);
using var reactive = new UiSurface(Rectangle(width: width));
reactive.SetViewport(new(800, 600));
width.Value = 40;
reactive.UpdateLayout();
```

標準Applicationは通常の更新点で自動適用する。Elementが購読を所有し、同じsourceの購読は
再構築でも継続する。引数が変わると新しいWidget設定を生成し、存続するElementへ採用する。
複数の部分木をまとめて検証・公開し、未変更の親・兄弟・子の実体を保持する。
現在の階層・設定・幾何を調べる時はRootElementとそのChildren / Widgetを使う。
Widget.Childrenは構築時の宣言であり、部分更新後の実体の一覧には使わない。

描画中にSignalの通知が届いても、現在の設定でframeを記録し、次の更新点で適用する。
独自hostも通常のUpdateTree / UpdateLayout / Record入口を使える。
[Signal連携のADR](../../../docs/adr/ui/UI-0019-signals-and-reactive-state.md)

Function components keep parameters in passive `UiFunctionWidget` declarations. The UI automatically runs
`Use` / `UseSignal` / `UseContext` inside a frame owned by the retained `UiFunctionElement`.
Its `Content` property exposes the currently mounted result. Nested functions get separate frames;
calling a definition directly shares its caller's frame. Applications use generated static factories.

## Retained drawing scene

```csharp
using var drawing = surface.CreateDrawingScene();
// Each update uses the existing mounted elements, dirty layout, and slot order.
using var source = drawing.Borrow(); // Update() + Scene.Borrow()
// await renderer.PrepareAsync(source, target, ...); keep source alive through preparation.
// Dispose source before the next Update(); retain/retire prepared frames using GPU completion.
```

The adapter owns only its DrawingScene2D. Scene may also be borrowed after an explicit Update,
or frozen with CaptureSnapshot; callers must not edit or dispose the adapter-owned Scene.
Snapshots and recordings do not extend borrowed texture/resource lifetimes: the caller keeps
those resources alive through every consuming GPU submission. Disposing the adapter does not
dispose the surface or external resources.

Default self-then-children elements get stable drawing groups, local immutable content, and
separate placement/clip metadata. LastRecordingCount exposes local capture work per update;
it is not a GPU draw count. Unknown custom OnRecord code runs on every update. A deterministic
custom element may override CacheRetainedDrawing and call InvalidateDrawing when local state
changes; ValidateRetainedDrawing can check external dependency revisions before layout.
Size, adopted configuration, and visual revision changes also revoke cached local content.
Rectangle drawing and empty base self drawing are reusable; built-in TextBox opts in and
validates its document/layout dependencies. Merely moving an unchanged leaf does not recapture it.
Custom RecordOverride implementations remain fresh opaque subtree recordings, preserving
arbitrary RecordSelf/RecordChild interleaving and inherited canvas state.

The standard Desktop/Browser Application host uses this adapter automatically for ordinary
frames, including auxiliary windows. UiSurface.Record and explicit Canvas/DrawList callers remain
unchanged. Active visual composition, page transitions, unknown visual hosts and visual exclusions
use fresh opaque recording through the existing composition path. An inactive standard visual
coordinator does not disable local reuse; visual layers and presentation receipts remain host-owned.

## Ordered box modifiers

C1 modifiers use the existing typed factory and `with` actions; no wrapper
Widget or Element is introduced. The first modifier is outermost:

```csharp
using Lumyte.UI;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32, with: [
    Align.Center(),
    Padding.All(12),
    Border.Stroke(new SRgba<float>(1, 1, 1, 1), 1),
    ConstrainedBox.Limits(new UiBoxConstraints(0, 240, 0, 80))
])[Rectangle(width: 16, height: 16)];
```

Repeated actions append independent immutable layers. Rebuild a fresh declaration
for changes; do not mutate a mounted declaration. Geometry belongs to each Element.
Border color changes invalidate drawing only; other modifier changes invalidate
layout, input and drawing. Insets must be finite and nonnegative, and alignment
coefficients must be finite values in the inclusive range 0–1.

`SizedBox` accepts zero or one child and uses inherited Width/Height as body
dimensions. `Separator` is a noninteractive horizontal (default) or vertical
line with explicit Color and nonnegative Thickness (default 1).

### Custom Element geometry

Parents still use child `Size` and set child offsets in their own body coordinates.
An Element's `Size`/`Bounds` describe its outer box; `ContentSize` describes the
body and `ContentBounds` gives its rectangle relative to the outer origin.
LayoutOverride receives body constraints exactly once. RecordOverride, OnRecord,
OnInput and OnProbePointerDown already receive body-local coordinates: do not
translate by ContentBounds again. Migrate self-drawing from
`canvas.FillRect(new(0, 0, Size.X, Size.Y), color)` to
`canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), color)`, and use
ContentSize for custom inside checks. Without modifiers both sizes remain equal.
ClipToBounds clips the outer box; modifiers add no implicit clip. Padding and
border do not enlarge the body's click target. Automation Bounds remains outer;
ContentBounds and ActionableBounds expose body geometry for point selection.
