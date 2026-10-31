# Lumyte.UI

Compositionで構築する保持型UIの基盤です。Panel、Stack、Rectangle、Button、背景スロット、親子関係、一方向layoutとCanvasへの描画記録を提供します。ポインター配送・capture・callback引数にも対応します。キーボードfocus・キー配送にも対応します。Window入力とDPIの接続は、UI本体から分離した[Native／Browserサンプル](../../../samples/Lumyte.UI.Shared/README.md)で提供します。

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

低水準APIは最初の実装PRから`surface.Rebuild(Build())`で新しい宣言を予約し、`surface.UpdateTree()`で適用します。同じ親・子slot内で型/key（keyなしは型/位置）を照合し、componentの`OnReconcile`で管理する内部状態を引き継ぎます。任意fieldの自動コピーは行いません。layout導入後は`UpdateLayout()`がこの境界を自動実行します。通常の子コレクションは各宣言につき一回だけ初期化します。Signalによる構築関数の自動再実行は下記のSignal節、静的関数Widget内の状態保持は関数Widget節を参照してください。

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

宣言プロパティのsetterはlayoutを無効化しません。値の変更は構築関数で新しいツリーを作ります。低水準APIでは新しい構築結果を`surface.Rebuild(root)`へ渡します。キーは同じ親・slot内で型と組み合わせて識別し、省略時は位置と型を使います。切替は次のUpdateLayoutで行い、Signalによる自動の再構築もこの経路を使います。接続・子の初回初期化・viewport・表示側のruntime状態が変わるとdirtyになり、次のUpdateLayoutでrootから更新します。cleanなUpdateLayoutは再実行しません。Size/Boundsの読取はlayoutを実行せず、計算した値は各Widgetへ直接保存します。例外時は更新を中断し、先行ノードの変更を戻しません。ホストは描画と通常入力を停止し、破棄するか原因を修正して明示的に更新し直します。

カスタムの葉はWidgetのLayoutOverrideを、カスタムコンテナーはPanelのLayoutOverrideを拡張します。直接の可視子をLayoutChildで一回ずつ処理し、その返値を使ってSetChildOffsetで一回ずつ配置します。宣言値は構築時に設定し、setterからInvalidateLayoutを呼びません。表示側のruntime状態を変更する専用経路ではInvalidateLayoutを使えます。処理中の宣言を変更せず、同じsurfaceの処理を重ねない順序で呼び出します。phaseや再入の検出は行いません。別threadへの順次移行は接続中も利用できます。

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

Recordは毎回、可視ツリーをコンポーネントが定義する順で記録します。既定は自身→登録された子で、Buttonが背景→自身→通常内容を指定します。独自コンポーネントはRecordOverrideで順序を定義し、RecordSelfとRecordChildを使用します。Fillはfiniteな[0,1]のstraight-alpha sRGBで、既定は透明です。色の検証はColor2Dの生成・init/withで行い、UIのsetterでは再検証しません。FillとClipToBoundsの変更はlayoutを無効化しません。viewportは常にclipされ、ClipToBoundsは自身と部分木を最終サイズの矩形へclipします。overflowした子のサイズは変更しません。

カスタムWidgetはOnRecord(DrawingCanvas2D)をoverrideし、原点0のローカル座標で記録します。親のclipは子の移動に追従せず、ホストのCanvas変換の内側へUIの原点を合成します。DPI変換はホストの責務です。OnRecordの変換・clipは内容や兄弟へ漏れません。Save/Restoreは釣り合わせ、借用中にBegin/End/Cancelを呼ばないでください。

描画対象を確定してから記録し、その間は対象を書き換えず、layout/Record/Disposeを重ねない呼出し契約です。phase検査で拒否する仕組みは設けません。例外時もCanvas状態を復元しますが、成功済み命令は残ります。frame全体をCancelしてからBeginで再試行してください。不均衡なSave/Restore等の契約違反による状態破壊は隔離しません。snapshotはsurfaceの変更・Dispose後も利用できますが、カスタム描画の借用GPU資源はGPU完了まで別途保持します。

RecordはCanvas、renderer、GPU、ClearやPresentを所有しません。単独UIでは各frameで描画先をClearし、重ね合わせではそのframeで再構成した背景へLoadします。複数surfaceのClearは最初に一度だけ行います。[描画サンプル](../../../samples/Lumyte.UI.Sample/README.md)に通常のCanvas→Renderer→Submit/Wait、viewport変更、親RenderGraph・Resources経由の描画を示します。UI本体の依存を増やさず、[サンプル補助](../../../samples/Lumyte.UI.Shared/README.md)でGPU完了までの所有権を管理します。

[ツリーのADR](../../../docs/adr/ui/UI-0002-widget-tree-and-composition.md)／[layoutのADR](../../../docs/adr/ui/UI-0003-layout-and-invalidation.md)／[描画のADR](../../../docs/adr/ui/UI-0004-drawing-and-clipping.md)

検証:

```sh
dotnet test tests/UI/Lumyte.UI.Tests -c Release
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release
```

命令snapshotの統合テストはDrawing2D.Testsに配置しています。実GPUの画素・連続frameの検証はGraphics.TestsのUiDrawingTestsを使い、実行環境で`LUMYTE_GRAPHICS_GPU_TESTS=1`を設定します。

子コレクションの名前と役割は各コンポーネントが定義します。カスタムコンポーネントはconstructorでCreateChildrenを呼び、ForegroundやEmbeddeds等のget-onlyプロパティへ保持できます。Widget基盤は登録された全コレクションを所有権・接続・解除のために走査し、Background専用の保存領域は持ちません。配置・描画順は各コンポーネントの責務です。

## ポインター入力

```csharp
using System.Numerics;
using Lumyte.UI;
using static Lumyte.UI.Compose;

int clicks = 0;
var button = Button(onClick: () => clicks++)[Panel(width: 80, height: 24)];
using var surface = new UiSurface(Panel()[button]);
surface.SetViewport(new Vector2(320, 200));
surface.UpdateLayout(); // 入力配送の前に配置と入力情報を更新
bool down = surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
bool pressed = button.IsPressed;
bool up = surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 10)));
// down/upはtrue、押下時のpressedはtrue、clicksは1。Up後のIsPressedはfalse。
surface.Dispatch(new UiFocusLostInput());
```

hostが対象surface、pointer ID、論理座標を選んで渡します。Window情報・DPI変換・ゲームへの入力消費の伝達は後続のhost接続の責務です。ホストは初回・表示状態変更後のUpdateLayoutを成功させてからDispatchします。Dispatchは配置・入力情報を更新せず、前面のhit要素から親へOnInputを配送します。透明な前面要素も背面のButtonを遮り、Buttonは自身の背景を入力へ公開しません。独自コンポーネントはGetInputChildrenで対象の子と後面→前面の順序を定義できます。返す列は重複のない直接の子だけとします。

capture中は外へ移動してもUp/Cancelまで配送し、IsPressedは最後の位置がButton・viewport・祖先clipの内側かを表します。Upではowner/captureを先に解除してから、その時点のOnClickを一度呼びます。nullでも入力消費は同じです。callbackの例外は伝播し、押下状態は残りません。callbackによる次のDispatchやDisposeをphase検査で拒否しません。通常利用ではモデルを更新し、次の構築結果へ反映します。

CancelとFocusLostはlayout・hit test・OnClickを実行せず、コンポーネントに喪失・取消を通知します。UpdateLayoutの例外ではその場で更新を中断し、途中結果を戻しません。hostは描画・通常入力を停止します。hostが配送を止める場合はFocusLostで残りを清掃し、消費済みの系列を物理終端までゲームへ転送しないよう管理します。Disposeも全captureを解除します。未処理の入力・未知pointer phaseはfalseです。作成threadへの固定、phase検査、lockは導入しません。

[入力のADR](../../../docs/adr/ui/UI-0005-pointer-dispatch-and-capture.md)／[callback生成のADR](../../../docs/adr/composition/COMPOSITION-0002-callback-parameters.md)

入力用の子列・geometry・clipは各Widget内に保存し、UpdateLayoutで更新します。独立した入力snapshotやButton矩形のmapは作りません。WidgetはButtonを参照・判定しません。UiSurfaceも具体的なコントロールを判定せず、hit要素から親へOnInputを配送します。Button自身がcaptureとClickを認識し、独自コントロールも同じ経路を使います。通常の値変更は再構築で扱い、コンポーネント専用のruntime入力更新はInvalidateInput、geometryも変わる場合はInvalidateLayoutを使います。いずれも次のUpdateLayoutで反映し、Dispatchでは再計算しません。GetInputChildrenは無効化まで安定した列を返してください。

[入力snapshotの性能測定](../../../docs/performance/ui-input-snapshots.md)／[再測定用ツール](../../../tools/benchmarks/ui-input/README.md)

## 独自コントロールの入力

Widgetの `OnInput(in UiInputContext context)` をoverrideします。`Unhandled` は親へ伝播、`Handled` は消費、`StopPropagation` は非消費で伝播を停止します。`PointerPosition` はclip前のローカル座標です。ポインターDown/Moveで `context.CapturePointer()` を呼ぶと、そのWidgetへ領域外のMove/Up/Cancelも直接届きます。capture配送は親へ伝播しません。同じコントロールの複数pointerも可能です。

`OnPointerCaptureChanged(UiPointerCaptureState state)` で取得・geometry更新・喪失を受け取れます。Up/Cancelは所有を先に解除してから `IsCaptured=true` のcontextで届きます。Clickの認識はButton側だけが行います。明示解放・対象除外・FocusLost・Disposeでも喪失とCancelを通知し、独自通知が失敗しても所有状態を残しません。

contextは配送中だけ使い、保存しないでください。pointer以外のUiInputも同じhookへ渡せます。通常キーとpointer以外の入力はfocus対象（なければroot）へ配送します。TextBoxとIME文書の編集は後続のTextEditing PRで追加します。

hit testでは、UpdateLayoutで各Widgetへ保存した「子のoverflowを含む部分木の入力領域」を使い、点を含まない枝を探索しません。clip・viewport・入力対象の選択を反映し、子の前後関係と親外のhitを保ちます。領域内の候補は従来どおり探索するため、重なりや多数の兄弟がある場合の最悪線形走査は残ります。

## キーボードとfocus

```csharp
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using static Lumyte.UI.Compose;

var button = Button(onClick: () => Console.WriteLine("Save"))[Panel(width: 100, height: 24)];
using var surface = new UiSurface(Panel()[button]);
surface.SetViewport(new(320, 200));
surface.UpdateLayout();
surface.Dispatch(new UiKeyInput(Key.Tab, true));
surface.Dispatch(new UiKeyInput(Key.Tab, false)); // surface.FocusedWidget == button
surface.Dispatch(new UiKeyInput(Key.Enter, true)); // IsPressed == true
surface.Dispatch(new UiKeyInput(Key.Enter, false)); // 所有解除後にcallbackを一回実行
```

Widget.Focusableは既定false、Buttonは既定trueです。Tab/Shift-TabはGetInputChildrenで公開した確定候補を巡回します。clip外・0寸法の候補も残し、非表示・無効な部分木やButton背景は除外します。ButtonはEnter/Spaceの押下とpointer押下を排他にし、UpでClickを認識します。capture中のpointerはTabでfocusを移しても維持します。

独自のfocusable WidgetはOnInput中にcontext.RequestFocusを呼べます。context.IsFocusedとUiFocusChangedInputで現在状態・通知を参照し、UiKeyInputを処理します。キーのrepeatやUpも受け取るにはcontext.CaptureKeyを使い、context.IsKeyCapturedで終端配送も識別します。UiKeyCancelInput・ReleaseKeyCapture・focus変更で認識を止めても、物理Upまではsurfaceが消費を保持します。FocusLost/Disposeは全状態を解除します。

WidgetがHandledまたはStopPropagationにしたTabでは標準巡回を行いません。キーや任意のUiInputが入力先で未処理なら所有親へ伝播します。キーから文字を合成せず、IMEは後続の文書/session経路で接続します。取得元Window・OS focus・修飾キー状態はhostが決めて渡します。UiKeyInputはInput.AbstractionsのKeyを使い、UIはInputSystemやOS backendへ依存しません。

Focusableを含む通常の値変更は新しい宣言を構築します。専用runtime更新で入力候補を変えたときはInvalidateInputと次のUpdateLayoutを使い、キー配送中にlayoutを実行しません。切断・破棄済みへのRequestFocusはfalseで、破棄済みsurfaceへのDispatch/FocusedWidget参照はObjectDisposedExceptionです。

[focusとキー入力のADR](../../../docs/adr/ui/UI-0006-focus-and-keyboard.md)／[keyboard-onlyサンプル](../../../samples/Lumyte.UI.Sample/README.md)

## 実Windowとの接続

[UI-0007](../../../docs/adr/ui/UI-0007-window-input-and-dpi-integration.md)では、取得originを持つInputRecordからUiInputへ変換し、NativeとHostedの系列ownerをホストで保持します。InputSystemのUpdate中はRecordedをqueueへ保存し、Updateが戻ってから配送します。DPIはCanvasへ一度だけ適用し、UiSurfaceは論理座標を扱います。UIパッケージのPlatform／Windowing／InputSystemへの依存は追加していません。

## 意味情報と自動操作の接続

`testId`、`automationName`、`role`は通常の構築引数です。PanelはGroup、ButtonはButtonを既定roleとします。roleは検索用の意味で、付与しただけでは操作を生成しません。TestIdの重複はscope付きLocatorで解決します。

`CaptureAutomationSnapshot`はlayoutを確定して、ID、意味情報、surface論理座標のBounds／ClippedBounds、祖先を含む状態を不変コピーにします。通常のDispatchにsnapshotを導入せず、要求時だけ取得します。IDはsurfaceへの接続寿命ごとに変わり、同じWidgetを再接続しても再利用しません。

独自部品はGetAutomationChildrenで直接の意味的な子と順序を選びます。既定は全登録コレクション、Buttonは背景を除くChildrenだけです。重複・直接の子以外・不正な座標を含むsnapshotは公開しません。実際の操作とpressed状態はAutomationActions、IsAutomationPressed、HasAutomationActivePressのoverrideで公開します。

`ProbePointer`は通常のhit testから親へ進み、OnProbePointerDownが最初に受理する部品のClick capabilityを返します。独自の入力消費部品はこのhookとOnInputで判定を共有してください。probeは副作用を起こさず、通常Downが止まる場所を親Buttonの操作点として返してはいけません。Buttonの型判定をWidget／UiSurfaceへ追加していません。

[Automationパッケージ](../Lumyte.UI.Automation/README.md)／[設計](../../../docs/adr/ui/UI-0009-ui-automation.md)

Signal引数は通常の静的factoryへ渡せます。

```csharp
using Lumyte.Signals;
using static Lumyte.UI.Compose;

var width = Signal.Create<float?>(80);
using var surface = new UiSurface(Panel()[Rectangle(width: width, height: 24)]);
width.Value = 120;
surface.UpdateLayout();
```

購読は接続中だけUIが管理し、更新は新しい宣言へ再構築します。動的な子は`new UiSurface(() => Build())`の構築関数からValueを読み、既存の型・key・slot照合で内部状態を保ちます。手書きnewを含むSignalツリーも構築関数を使ってください。標準Applicationはdispatcherとページ寿命を自動接続します。

## アニメーション

独立したモデルSignalを通常の`Animate`へ渡し、戻り値をWidget引数に指定します。標準Applicationが時計・フレーム・購読寿命を管理します。

```csharp
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;

var width = Signal.Create<float?>(80);
var animated = Animate(width, Duration.FromSeconds(0.2),
    UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
var root = Rectangle(width: animated, height: 24);
// callbackでwidth.Valueを変えると、現在の表示値から新しい目標へ補間する。
```

`UiAnimated.Select`は同じproducerの型変換を提供します。一般のComputedで包むと所属は伝わりません。モデルSignalは複数Windowで共有できますが、Animateはsurfaceごとに作ります。Timelineは`Animate(timeline)`から取得したclipの`Value(channel, initialValue)`を引数に指定し、Pause/Resume/Seek/Stop/Cancelをcallbackから予約できます。clipをページのfieldなどへ保持すると、構築関数の再実行でも同じ再生状態を使えます。

埋込みhostでは`surface.AdvanceAnimations()`をlayout前に一度呼びます。UpdateLayout/Record/Value読取だけでは時間は進みません。表示できない間は`SetAnimationUpdatesEnabled(false)`で休止します。標準Applicationでこれらを手動実行する必要はありません。

[値・TimelineアニメーションのADR](../../../docs/adr/ui/UI-0020-animation-and-frame-lifetime.md)／[決定的Headlessサンプル](../../../samples/Lumyte.UI.Application.Headless.Sample/README.md)

表示・退出には`isPresent: shownSignal`と`transitions: new UiElementTransitions { Enter = FadeIn(duration), Exit = FadeOut(duration) }`を静的構築関数へ渡せます。退出中はlayout領域を保持して入力を止め、最終通常frameの提示後に`onEntered`／`onExited`を通知します。再構築ではtype/key/slotが一致する状態を継承し、直接detachすると演出と未通知callbackを破棄します。標準Applicationがcaptureとframe更新を管理し、単独Surfaceや非対応描画では演出だけを省略します。`isVisible`は即時表示制御のままです。

通常の演出は保持した描画commandを再生し、半透明groupの中間bufferは内容変更時に更新します。custom描画もliveで参加でき、固定snapshotへのopt-inは不要です。決定的な描画commandのcacheと旧snapshotに対応する部品は`SupportsTransitionCapture`をoverrideし、独自の可変状態の変更を`InvalidateTransitionCapture()`で通知します。共有要素とページ演出の利用例は[transitions sample](../../../samples/Lumyte.UI.Transitions.Sample/README.md)を参照してください。

## 静的関数によるカスタムWidget

アプリケーションの部品は`Compose.Definitions`へ`[Composable] public static Widget Counter(...)`を書くことで定義できます。引数はパラメータ、戻り値はUIツリーになり、`using static`から生成factoryを普通の関数として呼びます。

```csharp
[Composable]
public static Widget Counter(string label, int start = 0)
{
    Signal<int> count = UseSignal(start);
    return Button(automationName: $"{label}: {count.Value}",
        onClick: () => count.Value++);
}

// using static Example.Compose;
// Counter(label: labelSignal, key: "counter");
```

`UseSignal`は`using static Lumyte.UI.Compose;`で使えます。同じ型・Key・子slot（Keyなしは位置）の関数なら、再構築で内部の値を維持します。lazy initializerも使えます。Key変更・削除後の再追加は初期化し直します。UIが関数内の直接読取と引数のSignalを購読し、関数の再実行・新しい子ツリーの生成・購読解除を自動管理します。スコープを手動で開く必要はありません。

関数bodyはsurfaceの接続またはRebuild準備で評価します。UseSignalはそのbody内で呼び、状態の更新は入力callback等から行います。明示的な`Signal.Create`は通常の独立値の生成であり、ローカル値の再利用を行いません。UseSignalの識別はソースの呼出位置を使うため、一行に複数の呼出しを書かず、ループ内では必要に応じて`key:`を指定します。Effectや外部資源の所有は既存のページcontext等で管理してください。

関数定義projectにはComposition GeneratorのAnalyzer参照が必要です。[動くサンプル](../../../samples/Lumyte.UI.Functions.Sample/README.md)と[API・状態規則のADR](../../../docs/adr/ui/UI-0023-function-widgets-and-local-signals.md)を参照してください。

共通の`Use(() => value)`で関数ごとの任意の値を保持でき、`UseSignal`もこの基盤を使います。初期化は同じ呼出位置の初回だけ行い、資源の自動破棄は行いません。親から借用値を渡す場合は、`UiContextKey<T>`を作り、`Provide(key, value, content)`と関数内の`UseContext(key)`を使います。子の関数へ自動的に引き継がれ、近い提供元が優先されます。

任意のNavigationパッケージには`UseNavigation()`を用意しています。ページhostがそのWindowのnavigatorを自動提供し、単独のSurfaceでは`ProvideNavigation(navigation, Home())`で明示提供できます。Use系はUIが同期的に関数bodyを評価している間だけ呼べます。

関数内のSignal変更は、その関数境界の宣言とbodyだけを再構築します。無関係な兄弟とrootは保持し、dirtyな親子は親の更新へまとめます。通常のWidget引数も最も近い関数の依存になります。明示的なRebuild、関数外の依存、全体検証が必要な部品・共有要素keyがある場合は全体再構築へ戻ります。独自WidgetはSupportsLocalDeclarationValidationで、検証が部分木内に閉じることを明示して参加できます。公開の子collectionは一回初期化のままです。geometryの変更は祖先のlayoutと入力計算へ伝播します。
