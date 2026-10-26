# ADR-UI-0001: Compositionで構築する保持型UIシステム

- 状態: 提案
- 日付: 2026-10-26

## 背景

Lumyteには宣言的構築のComposition、入力履歴、Native Windowと任意のWindowing管理、2D描画、アニメーション、リソースの世代管理がある。これらの上に、ゲーム内の設定画面やツールのパネルを構築するUI層を設けたい。既存機能をUI内で再実装せず、子要素の配置、入力対象、フォーカス、表示更新を同じツリーから決める必要がある。

[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)は構築構文と操作の生成を提供するが、親子関係、差分更新、値の保存、描画、寿命を管理しない。UIはこれらの責務を所有し、Compositionを実際の組み立ての入口として使う。本ADRはその基本モデルと初期公開境界を提案する。Compositionを使う方針は要求事項であり、以下のUI固有の契約はレビュー対象である。

## 決定

### 配置・依存関係と適用範囲

`src/UI/Lumyte.UI`、assembly・名前空間・NuGet名を`Lumyte.UI`とする。.NET 10のmanagedライブラリとし、実行時の依存はComposition、Input.Abstractions、Graphics.Drawing2Dとそれらの推移依存に限定する。GeneratorはAnalyzer参照する。下位ライブラリからUIを参照せず、CompositionのBCLのみという依存境界を維持する。

```text
アプリ / Engineの統合
  ├─ Platform・IWindow / 任意のWindowSystem → 表示先・論理サイズ・入力先の決定
  ├─ InputSystem → UI入力への変換 → UiSurface
  ├─ Compositionの生成ファクトリ → Widgetツリー → レイアウト・入力・描画記録
  └─ Canvas2D / RenderGraphCanvas2D → 既存renderer → GPU実行・提示
```

UI本体はPlatform、Windowing管理、InputSystem、Graphicsバックエンド、RenderGraph、DI、Resources、Animation、Diagnosticsへ直接依存しない。必要な接続は利用側か後続の任意パッケージに置く。既存ライブラリのAPIや責務は変更しない。

初期対象は、保持型ツリー、重ね合わせPanel、縦横Stack、矩形、単一内容と背景スロットを持つButton、矩形クリップ、ポインターとキーボードによる操作である。カスタムWidgetは計測・描画を拡張できる。Grid、ScrollView、仮想化、データバインディング、テーマ、Text・Imageコントロール、編集・IME、アクセシビリティ、UI診断は後続設計とする。文字や画像の配置を担う下位描画APIが存在することと、それらのUI契約が完成していることは区別する。

### Compositionによる組み立て

`public static partial class Compose`内の`public static partial class Definitions`に、publicなWidget基底型と`[Composable] public partial`な具体型を置く。引数なし構築を可能にし、設定は`[ComposeParameter]`、通常の子は`[ComposeContent]`、操作は`[ComposeAction]`、背景は`[ComposeSlot]`で宣言する。初期値は定義型で与える。

利用側は`using Lumyte.UI; using static Lumyte.UI.Compose;`を使う。生成ファクトリの呼び出しは毎回新規Widgetを作り、毎フレームの再構築や自動reconciliationは行わない。更新は保持したWidgetのプロパティや子リストを変更する。

```csharp
var marker = Rectangle(width: 24, height: 24,
    fill: new Color2D(1, 1, 1, 1));
var button = Button(width: 160, height: 48,
    with: [Button.OnClick(() => settings.Toggle())])[
        marker,
        Button.Background()[Rectangle(fill: new Color2D(0.1f, 0.2f, 0.4f, 1))]
    ];
var root = Stack(spacing: 8)[button];
using var surface = new UiSurface(root);
```

例には`Lumyte.Graphics.Drawing2D`のusingと利用側の`settings`が必要である。通常の子とスロットを混在できる既存Generatorをそのまま使い、別のUI用Generatorや文字列マークアップは作らない。`with`は引数適用後、子インデクサーより先に実行されるため、`OnClick`は購読だけを行い、構築時にClickやレイアウトを実行しない。

### ツリー・値・寿命の所有権

Widgetの参照同一性を状態の識別に使う。一つのWidgetを複数親、複数スロット、複数surfaceに同時配置できず、循環も禁止する。背景スロットも同じ所有ツリーの一部であり、通常の子より先に描画する。

UI側のChildren setterとスロット操作は、入力列をコピーし、null、重複、循環、他の親・surfaceへの所属、Buttonの通常内容が高々一つという制約を検証してから置換する。検証失敗は`ArgumentException`であり、その置換の前のツリーを保つ。返すリストは内部配列を書き換えられない読み取り専用ビューとする。これは生成インデクサーが配列をそのままsetterへ渡す契約と両立する、UI側の保存方針である。

Compositionは通常内容とスロットを順次適用するため、式全体のトランザクションは保証しない。後のスロットが失敗しても先に成功した通常内容の置換は残る。別の親へ移すときは、元の親から外してから接続する。取り外した部分木内の親子関係と値は保持し、別のsurfaceへ再接続できる。

UiSurfaceは接続中のフォーカス・capture・操作状態を管理する。取り外し、無効化、非表示化では対象部分木のそれらを解除し、押下を取り消す。Widget自体はGPU資源を所有しない。surfaceのDisposeはツリーとの接続を解除し、Widget、Window、入力source、外部の購読先やリソースを破棄しない。利用側のイベント購読は利用側が解除する。

### レイアウトと無効化

全座標は右・下を正とする論理pixelとする。Width/Heightのnullは自動、非nullは有限の非負値で、NaN・Infinity・負数を拒否する。明示寸法も親からの利用可能寸法を上限とする。UiSurfaceのviewportは有限の非負サイズ、scaleは各軸で有限の正数とする。

レイアウトはMeasure→Arrangeの二段階とする。MeasureOverrideへ渡す制約と返す希望サイズは有限の非負値。Panelは各子を同じ制約で計測して最大値を希望サイズとし、子を同じ領域へ重ねる。Stackは主軸方向の希望サイズとSpacingを合計し、交差軸の最大値を取る。主軸の計測には親の全制約を渡し、配置時には前の子から残り領域へ収める。余剰領域の主軸方向への均等分配は行わない。子は交差軸にstretchするが、明示寸法はその上限までとする。

Buttonは内容の希望サイズを採用し、背景は希望サイズへ加算せず確定した領域へstretchする。Rectangleの自動寸法の希望値は0とする。ルートはviewportへ配置する。各子のBoundsは親のローカル座標であり、描画とhit testは同じ累積位置を使う。

寸法・子構成・Stack設定の変更は自身と祖先のMeasure/Arrangeを無効化し、Fillの変更は描画だけを無効化する。描画命令はRecordごとに再記録し、GPU差分更新やDrawListの自動cacheは初期対象外とする。初期実装ではdirtyなsurfaceのレイアウトをルートから再計算してよい。DispatchとRecordは必要なら先にUpdateLayoutを行い、古いBoundsを使わない。

### 入力配送・フォーカス

InputRecordにはWindowの配送先が含まれないため、UIはDeviceIdからWindowを推測しない。利用側がイベント時点の表示先を決定し、当該surfaceの論理座標へ変換してDispatchする。InputSystemの記録は消費・削除しない。ゲームへの配送抑制はDispatchの戻り値を使って利用側が決める。配送先不明の入力を全surfaceへ配る実装はしない。

初期のポインターIDはsurface内で一意なlongとし、利用側がmouse、touch、penの識別子から安定して割り当てる。主ボタン以外、wheel、筆圧、gestureは後続の入力拡張とする。hit testは描画順の逆順で、祖先のclipを満たす可視・有効・HitTestVisibleな要素から最前面を選ぶ。HitTestVisible=falseは部分木全体を除外する。背景スロットは装飾であり、入力対象とフォーカス対象にはしない。矩形の左・上端を含み右・下端を除く。透明なFillだけでは入力を透過しない。

Downの操作対象はhitした要素から祖先を辿った最寄りのButtonとする。これによりButtonの内容上でもクリックでき、入れ子のButtonでは内側だけが処理する。Buttonに属さない前面要素から背面のButtonへ透過させない。Buttonは主ポインターDownでフォーカスとcaptureを取り、同じIDのUpが自身と祖先clipの領域内ならClickを一度発火する。移動中もcapture先へ配送し、Cancel、フォーカス喪失、取り外しでClickせず解除する。一つのButtonでは最初のポインターまたはキーだけが押下を所有し、他の開始入力は押下終了まで無視する。キー操作はフォーカス中のEnter/Spaceの非repeat Downで開始し、同じキーのUpで一度Clickとする。Tab等でフォーカスが別要素へ移った場合も元のキー押下を取り消す。

Tab/Shift+Tabはツリーの通常内容の前順でFocusableな可視・有効要素を巡回し、端で循環する。KeyDataのKey/IsDown/IsRepeatからUiKeyInputを作り、Shift等の状態は利用側が追跡して渡す。FocusLostはすべての押下・capture・フォーカスを解除する。一般のrouted event、任意のcapture操作、modalやWindowを跨ぐフォーカス移動は後続設計とし、初期はButtonのClickを意味イベントとして公開する。

### 描画・既存システムとの接続

UiSurface.RecordはBegin済みのDrawingCanvas2Dへ、背景→自身→通常内容の順に記録する。各Widgetのローカル原点を適用し、ClipToBoundsが有効なら矩形clipを追加する。surfaceのviewport自体もclipする。Save/Restoreを対応させ、呼び出し元の状態を戻す。CanvasのBegin/End、GPU準備、Submit、Wait、Presentは呼び出し元が行う。

WindowState.ClientSizeをviewport、ScaleをUI外側の描画変換とDrawingTargetInfo2DのPixelScaleへ反映する。scaleをUI内部で再乗算しない。入力は既に論理pixelなら再除算しない。Hostedでは利用側がWindowNodeの位置とclipを合成して同じ規則を使う。Native/Hostedごとの表示順と配送先の管理は[WINDOW-0001](../window/WINDOW-0001-window-system.md)へ委ねる。

通常Canvas2Dでは[GRAPHICS-0018](../graphics/GRAPHICS-0018-2d-graphics-engine.md)の直接描画、RenderGraphCanvas2Dでは[GRAPHICS-0022](../graphics/GRAPHICS-0022-2d-render-graph-layers.md)の親graphへの接続を使える。UI本体はlayerやgroup opacityを要求せず、子のalphaの乗算をgroup opacityの代用にしない。

将来のTextは、外部文字ライブラリがshaping・fallback・改行・glyph配置・atlasを担当し、[GRAPHICS-0021](../graphics/GRAPHICS-0021-2d-glyph-representations.md)の汎用描画へ変換する。Imageも既存画像描画へ接続する。UI本体にfont renderer、独自のGPU資源managerやdevice復旧処理を設けない。外部textureやatlasとleaseはGPU完了まで利用側が保持する。deviceロスト後もCPUツリーを維持できるが、描画側のprepared、bindings、device資源は既存Graphics/Resources契約に従って作り直す。

### スレッド・失敗の契約

UiSurfaceと接続したツリーはsurface作成スレッドからのみ操作する。切り離したツリーも同時操作はできず、接続前の受け渡しの同期は利用側が保証する。入力callbackはDispatch内で同期実行する。Layout/Record中のツリー・プロパティ変更、Dispatch/UpdateLayout/Recordの再入、所属先に反する操作は`InvalidOperationException`とする。

Click callbackでは通常のプロパティ変更や子の置換を許容する。Click前に押下とcaptureを解除し、callback後に取り外した対象へ操作を継続しない。callbackの例外は伝播し、既に行った変更は戻さない。不正な数値やenumは`ArgumentOutOfRangeException`、必須のnullは`ArgumentNullException`とする。無効なoverrideの計測結果は`InvalidOperationException`とし、次回レイアウトを必要な状態に保つ。

描画失敗は例外を伝播する。成功済みのCanvas命令は巻き戻せないため、そのframeのCanvasは利用側がCancelして再記録する。Disposeは冪等で、その後のsurface操作は`ObjectDisposedException`とする。破棄時にユーザーcallbackは実行しない。

## 公開APIの提案

比較元はmainの`16d856a0e9cda6a6ecfa559aaf55d87b4085b139`。同revisionにLumyte.UIはなく、以下は追加する主要宣言の抜粋であり未実装である。Vector2、KeyはそれぞれSystem.Numerics、Lumyte.Input.Abstractions、描画型はLumyte.Graphics.Drawing2Dの型を使う。

```diff
+namespace Lumyte.UI
+{
+    public enum UiOrientation { Vertical, Horizontal }
+    public enum UiPointerPhase { Move, Down, Up, Cancel }
+    [Flags]
+    public enum UiModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Meta = 8 }
+    public abstract record UiInput;
+    // Positionはsurfaceの論理座標。Down/Upは主ボタンのみ。
+    public sealed record UiPointerInput(long PointerId, UiPointerPhase Phase,
+        Vector2 Position) : UiInput;
+    public sealed record UiKeyInput(Key Key, bool IsDown, bool IsRepeat,
+        UiModifiers Modifiers) : UiInput;
+    public sealed record UiFocusLostInput : UiInput;
+
+    public sealed class UiSurface : IDisposable
+    {
+        // 親もsurfaceも持たないrootを接続。nullは拒否。
+        public UiSurface(Compose.Definitions.Widget root);
+        public Compose.Definitions.Widget Root { get; }
+        public Compose.Definitions.Widget? FocusedWidget { get; }
+        // sizeは非負、scaleは正、全成分finite。scaleは描画変換を行わない。
+        public void SetViewport(Vector2 logicalSize, Vector2 scale);
+        // 未設定viewportは0×0。dirtyなレイアウトを確定する。
+        public void UpdateLayout();
+        // UIが押下・capture・focus移動等として処理したときtrue。
+        // 対応しないキー等はfalse。nullは拒否。
+        public bool Dispatch(UiInput input);
+        // Begin済みCanvasを借用。Begin/End/Submitはしない。
+        public void Record(DrawingCanvas2D canvas);
+        public void Dispose();
+    }
+
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                protected Widget();
+                public Widget? Parent { get; }
+                public Vector2 DesiredSize { get; }
+                public Rect2D Bounds { get; }
+                // 既定null。setterが数値検査とレイアウト無効化を行う。
+                [ComposeParameter] public float? Width { get; set; }
+                [ComposeParameter] public float? Height { get; set; }
+                // 既定true。falseの部分木は描画・計測・入力から除く。
+                [ComposeParameter] public bool IsVisible { get; set; }
+                // 既定true。falseの部分木は入力対象外、描画は維持。
+                [ComposeParameter] public bool IsEnabled { get; set; }
+                // 既定true。falseの部分木はhit test対象外。
+                [ComposeParameter] public bool HitTestVisible { get; set; }
+                // 既定false。子への矩形clipもhit testと一致する。
+                [ComposeParameter] public bool ClipToBounds { get; set; }
+                // 既定false。Buttonはコンストラクターでtrueにする。
+                [ComposeParameter] public bool Focusable { get; set; }
+                protected void InvalidateMeasure();
+                // 基底実装は希望サイズ0。公開寸法の制約はUI側で適用する。
+                protected virtual Vector2 MeasureOverride(Vector2 availableSize);
+                protected virtual void ArrangeOverride(Vector2 finalSize);
+                // 原点・clip適用済み。Begin/Endと不均衡なSave/Restoreを禁止する。
+                protected virtual void OnRecord(DrawingCanvas2D canvas);
+            }
+            [Composable]
+            public partial class Panel : Widget
+            {
+                public Panel();
+                // コピー・所有権検証・親付けはsetterの責務。既定空。
+                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
+            }
+            [Composable]
+            public partial class Stack : Panel
+            {
+                public Stack();
+                // 既定Vertical。
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                // 既定0。finiteかつ非負。
+                [ComposeParameter] public float Spacing { get; set; }
+            }
+            [Composable]
+            public partial class Rectangle : Widget
+            {
+                public Rectangle();
+                // 既定は透明。既存Color2Dと同じ色の契約。
+                [ComposeParameter] public Color2D Fill { get; set; }
+            }
+            [Composable]
+            public partial class Button : Widget
+            {
+                public Button();
+                // 通常内容は0〜1個。背景も同じツリー所有権で検証する。
+                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
+                public IReadOnlyList<Widget> BackgroundChildren { get; }
+                public bool IsPressed { get; }
+                public event Action? Click;
+                [ComposeSlot]
+                private static void Background(Button target, IReadOnlyList<Widget> children);
+                // callbackがnullなら適用時にArgumentNullException。
+                // 生成拡張はAction<Button>を返し、適用のたびに購読する。
+                [ComposeAction]
+                private static void OnClick(Button target, Action callback);
+            }
+        }
+    }
+}
```

上記定義から生成される利用側の入口は次の形になる。他のComposeParameterも既存Generatorの規則で引数へ反映する。継承したWidth/Height等も同じ規則で扱う。

```diff
+namespace Lumyte.UI
+{
+    public static partial class Compose
+    {
+        // キャッシュした専用delegate。呼び出しごとに新規ノード。
+        public static Definitions.StackFactory Stack { get; }
+        public static Definitions.RectangleFactory Rectangle { get; }
+        public static Definitions.ButtonFactory Button { get; }
+    }
+    public static class ComposeButtonCompositionExtensions
+    {
+        public static Action<Compose.Definitions.Button> OnClick(
+            this Compose.Definitions.ButtonFactory factory, Action callback);
+        public static CompositionSlot<Compose.Definitions.Button, Compose.Definitions.Widget>
+            Background(this Compose.Definitions.ButtonFactory factory);
+    }
+}
```

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 毎frameの即時モードUI | 既存Compositionは新規生成の契約であり、状態と親子関係を維持するには別の識別・照合機構が必要になる |
| 手動のnew/Addだけを公開する | 子・操作・スロットを統一する既存Compositionを活用できず、構築の入口が分散する |
| CompositionにUIツリー・レイアウトを追加する | BCLのみの汎用構築基盤という依存と責務を変える |
| UIがWindow・入力pump・GPU実行を一括所有する | 既存のホスト構成と所有権を重複させ、単一WindowやRenderGraphなしの利用を難しくする |
| 初期からText・IME・全コントロールを一括設計する | 外部文字処理、編集、プラットフォーム連携まで一つの判断へ混在させる |

## 結果と影響

既存のComposition構文でUIを組み立て、保持した同じノードを更新できる。レイアウト、描画、hit testが一つのツリーを共有し、直接描画とRenderGraphの双方へ接続できる。既存の下位ライブラリへUI依存を追加しない。

一方、Widget・子リストのコピー・withのActionに割り当てがあり、最初の実装はsurface全体の再レイアウト・再記録を許容する。大量一覧の性能は別途仮想化と測定が必要である。データからUIを再生成したときの状態移行も自動では行わない。Text、テーマ、編集などを追加するまでは、基本部品とカスタムWidgetによるUIが提供範囲となる。

## 検証方針

実装PRでは`tests/UI/Lumyte.UI.Tests`と`samples/Lumyte.UI.Sample`を追加し、次を受け入れ条件とする。本PRは設計文書のみで、これらの実装・検証済みを意味しない。

- 実際のGeneratorを使い、継承パラメーター、with、通常内容と背景スロットの混在をコンパイル・実行する。
- 子配列の外部変更の遮断、重複・循環・複数親の拒否、置換単位の失敗保証、スロット失敗時の部分適用を確認する。
- Measure/Arrange、幅0、非表示、入れ子Stack、clip、resize、無効化後のDispatchとRecordを決定的なCPUテストで確認する。
- 複数pointer、capture中の領域外移動、Cancel、取り外し、無効化、FocusLost、Tab巡回、キーrepeatを検証する。イベントcallbackの例外・再入も含める。
- 描画命令列で親子の位置・clip・背景順とSave/Restoreを確認し、直接描画と親RenderGraph接続は既存GPUテスト基盤で確認する。
- Windows/Linux/Browserで論理座標・DPI・入力先と終了順を検証する。AOT/trimmingと性能は実測し、未検証環境をREADMEで明示する。

## 別途決定する事項

- Text・Imageの資源接続、文字計測と描画の一致、font fallback・shaping・atlasの寿命。
- Grid、padding/margin、整列、scroll、仮想化、テンプレートとテーマ、プロパティ優先順位。
- データバインディング、差分更新と状態保存、Animationとの任意接続。
- 一般イベント経路、modal、popup、IME・clipboard・テキスト編集、アクセシビリティ。
- DI/Executionの登録、Diagnostics、性能予算とcacheの世代管理。これらはUI本体に必須依存を追加する前に個別ADRで決める。
