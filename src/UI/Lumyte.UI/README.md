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
RectangleのFillはWidgetの設定、Size / BoundsはElementの幾何を使う。色はColor2Dで検証する。
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
