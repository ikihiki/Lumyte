# ADR-UI-0025: 借用した文字・画像と進捗を同じ表示寿命へ接続する

- 状態: 採用
- 日付: 2026-11-05
- 基準コミット: `d82c5c185f9d4f0218442ada9a9a0132e0fb51f8`

実装は未マージであり、以下はこの変更の設計決定を示す。

## 背景

画面の作成者は、一般の文章、準備済み画像、意味付きアイコン、進捗を表示するとき、基準コードでは独自Elementを実装しなければならない。編集文書を必要としない文章までTextBoxで代用すると、編集・入力と表示の責務が混ざる。

[UI-0013の表示部品](UI-0013-component-catalog-and-delivery-plan.md#2-表示装飾進捗-8種類)の5種類を具体化する。[UI-0002](UI-0002-widget-tree-and-composition.md)、[UI-0003](UI-0003-layout-and-invalidation.md)、[UI-0004](UI-0004-drawing-and-clipping.md)、[UI-0008](UI-0008-render-graph-and-resource-lifetime.md)、[UI-0019](UI-0019-signals-and-reactive-state.md)、[UI-0020](UI-0020-animation-and-frame-lifetime.md)、[UI-0024](UI-0024-ordered-layout-and-decoration-modifiers.md)の所有権と更新境界を維持する。基準コードの `src/Graphics/Lumyte.Graphics.Text/TextLayout2D.cs`、`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Image2D.cs`、`src/UI/Lumyte.UI/UiSurface.Animations.cs` を再利用する。

## 決定

任意のLumyte.UI.MediaへText、Image、Icon、ProgressBar、BusyIndicatorを置き、通常の型付きComposition factoryとUiArgumentを使う。文字組みは明示的に借用する一般のprovider、画像は準備済みの不変snapshot、時間は既存surfaceのanimation phaseを使い、MediaからPlatform、Resources、DI、テーマ、TextEditingへ依存しない。

### 利用者の変更前・変更後

変更前は独自ElementのOnRecordから `canvas.DrawText(layout)` や `canvas.DrawImage(image, source, destination)` を呼べるが、幅で変わる文字組みと宣言差分の分類は利用者が実装する。基準コードの `src/Graphics/Lumyte.Graphics.Text/TextCanvas2DExtensions.cs` は文字snapshotを描くだけである。

変更後はフォントをアプリが登録した借用engineからproviderを作る。engineとフォント登録、frameのBegin/Endは以下では省略する。

```csharp
using Lumyte.UI;
using Lumyte.UI.Media;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

var provider = new ParleyTextLayoutProvider(engine);
var content = Stack()[
    Text(value: "幅に応じて折り返す文章", layoutProvider: provider, width: 160),
    ProgressBar(value: 0.5),
    BusyIndicator(automationName: "読み込み中")
];
using var surface = new UiSurface(content);
// 通常hostは自動実行。埋込みhostはowner loopのlayout前に一回呼ぶ。
surface.AdvanceAnimations();
```

同じ戻り値型へのwith適用と子slotの既存規則を変えない。余白は `with: [Padding.All(8)]` で付ける。

### 文字組みproviderの契約

ITextLayoutProvider.CreateLayoutは文字列と最終の最大本体幅を受け、一回の同期呼び出しでGraphics.Textの不変TextLayout2Dを返す。TextDocument、selection、caret、TextBoxは一切必要としない。有限の正幅と正の無限大を許可し、0幅では描画なしの0幅snapshotを返す。返却値はprovider・fontの解放後も独立して使え、外部GPU paintがある場合だけ後述の借用寿命を要求する。例外では今回のlayoutを公開せず既存layout失敗規則に従う。

Textはadvance寸法を親制約へclampし、文字を再縮尺しない。overflowは明示のClipToBoundsで制御する。値/provider変更はlayout、描画設定変更はvisualの無効化とし、getterやRecordから文字組みを再実行しない。任意のLumyte.UI.Media.Parleyは既存ParleyTextEngine2Dを借用し、設定listをコピーする。暗黙のOSフォント、同期ファイルload、代替の文字エンジンを追加しない。[GRAPHICS-0024](../graphics/GRAPHICS-0024-text-rendering.md#不変なレイアウトと描画)のsnapshot契約を維持する。

### 画像・アイコンとGPU借用

PreparedImageはImage2D、正の有限なintrinsic logical寸法、source texel矩形を保持する不変の値である。未供給のnullは描画せず、明示のPlaceholderSize（既定0）で占有する。snapshot差替えは通常の宣言またはSignal更新でlayoutを無効化し、load完了がUIを直接変更しない。

ImageはNone、Fill、Uniform、UniformToFillの伸縮を明示する。Noneはintrinsic寸法、Fillは非等方、Uniformは内接、UniformToFillは外接で、いずれも中央配置する。片軸だけtightなら比率から他軸の希望寸法を求め、最後に親制約へclampする。両軸tightでは本体寸法を優先する。0面積は除算せず描画しない。clipは既存ClipToBoundsを使い既定trueとする。

UIは画像のview、sampler、allocation、resource leaseを所有も解放もしない。利用者は記録した直接DrawList、retained scene、capture、準備済みframeを使用し終えるまでsnapshotの資源ownerを保持し、最後のGPU完了を確認した後だけ返す。UIのunmount、Signal差替え、DisposeはGPU完了ではない。Prepare失敗なら未送信commandを先に破棄し、Submit後のWait取消ではownerを退役待ちへ残す。自動refcountによる安全性を装わず、既存DrawingSnapshot2DとUI-0008のframe coordinatorをそのまま使用する。

Iconはvector Path2D、glyph TextLayout2D、PreparedImageのいずれか一つを不変IconSourceに保存し、正の有限なviewBoxからSize（既定16）の正方形へUniform配置する。Sizeは要求寸法であり親tight制約が優先する。accessible nameは既存AutomationNameで明示する。装飾用の空名も許可し、Icon単体にはButton動作やfocus/activationを付けない。

同じsourceでもshared-rootのtight寸法変更で正規化した描画は変わり得るため、ImageとIconはdestination captureの再利用を許可せず旧側をcaptureする。Uniform、UniformToFill、NoneだけでなくFillも保守的に同じ扱いとし、vector・glyphも例外にしない。

### 進捗・時間と意味情報

ProgressBar.Valueはfiniteな0〜1、既定0とする。範囲外、NaN、Infinityは公開前にArgumentOutOfRangeExceptionとしclampしない。既定の要求寸法120×4へ親制約を適用し、本体へ背景と横方向の進捗を描く。callback、drag、keyboard編集は持たず、snapshotへValueとProgressBar roleを公開する。

BusyIndicatorは既存surfaceの借用単調時計とowner phaseに登録する受動的な時間参加者を使う。独立timer、thread、dispatcher予約を作らず、標準hostのHasRunningAnimationsへ参加し、AdvanceAnimationsだけが値を進める。mountedで可視の祖先を持つときだけ進行し、非表示・surface休止では位置を凍結、再開時は休止時間を加算しない。unmountで登録を解除する。同じ宣言を別surfaceへ接続しても位相は独立する。

時間参加者の追加はUI coreの汎用拡張点でありMediaへの参照を追加しない。phase例外は既存animation fault規則へ接続する。null休止通知も同じ失敗をlatchし、後続の参加者と既存value producerの休止を試みてから最初の例外を再送出する。通知中のanimation phase再入は拒否し、unmount・Dispose後のsnapshot要素は呼び出さない。描画位相が変わるとInvalidateDrawingを呼び、layoutは変えない。busy状態はIsIndeterminateとして公開し、時間や状態のgetterは時計を読まない。全5種類のsemantic capabilityはNone、focus適格性もfalseであり、roleやFocusableを指定してもClick等の閉じた操作集合を増やさない。wireの任意フィールドvalue/isIndeterminateと5つのrole文字列を追加し、既存versionと操作名を維持する。旧clientは追加フィールドを無視し、新clientは値のない既存nodeをnull/falseとして読む。

### 公開API差分

基準コードに対する追加の主要宣言を示す。型名はSystem.Numerics、Graphics.Drawing2D、Graphics.Text、Core.Timeの既存型を使う。

```diff
+namespace Lumyte.UI
+{
+    public partial class UiElement
+    {
+        // 接続時に一回判定する受動的参加。既定false、getterは副作用なし。
+        protected virtual bool HasFrameAnimation { get; }
+        // owner animation phaseのみ。nullは休止、再開時catch-upなし。
+        protected virtual void OnAnimationFrame(TimePoint? now);
+        // 既定null/false。getterは副作用なし。redactedでは読み取らない。
+        protected virtual double? AutomationValue { get; }
+        protected virtual bool IsAutomationIndeterminate { get; }
+    }
+    public sealed class UiAutomationNode
+    {
+        public double? Value { get; }
+        public bool IsIndeterminate { get; }
+    }
+    public enum UiRole { Text, Image, Icon, ProgressBar, BusyIndicator } // 既存値を維持して追加。
+}
+namespace Lumyte.UI.Media
+{
+    public interface ITextLayoutProvider
+    {
+        // 借用provider。同期で独立した不変snapshotを返す。
+        TextLayout2D CreateLayout(string value, float maximumWidth);
+    }
+    public sealed class PreparedImage
+    {
+        // 資源は借用。最後の消費frameのGPU完了までownerを保持する。
+        public PreparedImage(Image2D image, Vector2 intrinsicSize, Rect2D source);
+        public Image2D Image { get; }
+        public Vector2 IntrinsicSize { get; }
+        public Rect2D Source { get; }
+    }
+    public enum ImageStretch { None, Fill, Uniform, UniformToFill }
+    public sealed class IconSource
+    {
+        public static IconSource Vector(Path2D path, Rect2D viewBox, PaintGraph2D paint);
+        public static IconSource Glyph(TextLayout2D layout, Rect2D viewBox);
+        public static IconSource Image(PreparedImage image);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            [Composable] public partial class Text : Widget
+            {
+                public Text();
+                [ComposeParameter] public string Value { get; set; } // 既定空文字。null拒否。
+                [ComposeParameter] public ITextLayoutProvider? LayoutProvider { get; set; } // 接続時必須。
+            }
+            [Composable] public partial class Image : Widget
+            {
+                public Image();
+                [ComposeParameter] public PreparedImage? Source { get; set; }
+                [ComposeParameter] public Vector2 PlaceholderSize { get; set; } // finite・非負。
+                [ComposeParameter] public ImageStretch Stretch { get; set; } // 既定Uniform。
+            }
+            [Composable] public partial class Icon : Widget
+            {
+                public Icon();
+                [ComposeParameter] public IconSource? Source { get; set; }
+                [ComposeParameter] public float Size { get; set; } // 正のfinite、既定16。
+            }
+            [Composable] public partial class ProgressBar : Widget
+            {
+                public ProgressBar();
+                [ComposeParameter] public double Value { get; set; }
+                [ComposeParameter] public Color2D Foreground { get; set; }
+                [ComposeParameter] public Color2D Background { get; set; }
+            }
+            [Composable] public partial class BusyIndicator : Widget
+            {
+                public BusyIndicator();
+                [ComposeParameter] public Color2D Color { get; set; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.Media.Parley
+{
+    public sealed class ParleyTextLayoutProvider : ITextLayoutProvider
+    {
+        // engineを借用、optionsのlistをコピー。font所有者は呼出し元。
+        public ParleyTextLayoutProvider(ParleyTextEngine2D engine, ParleyTextOptions2D? options = null);
+        public TextLayout2D CreateLayout(string value, float maximumWidth);
+    }
+}
```

## 検討した代替案

- TextBoxのproviderを共有する: 既存実装を使えるが編集文書とcaret契約を一般表示へ強制するため、Graphics.Text snapshotを直接返す独立interfaceを選ぶ。
- Mediaが画像ownerを自動解放する: unmountとGPU完了の寿命が異なり、retained/captureの消費を追跡できないため、既存の明示借用とframe coordinatorを維持する。
- 部品ごとのtimer: 実装を局所化できるが非表示、navigation、複数surfaceの休止を迂回するため、既存owner loopへ参加する。

## 結果と影響

5種類を同じ宣言・Element・Signal・with・記録経路から使える。UI coreはMediaを参照せず、文字の実装とnative依存は任意adapterに限定する。利用者はproviderの供給と画像ownerのGPU完了までの保持を明示する責任を持つ。

受け入れ条件は実Parleyの折返しと一回layout、font/provider失敗後の再試行とcleanup、画像の伸縮・clip・未供給、GPU完了までのowner保持、iconの3表現と意味名、進捗の値境界とSignal更新、明示時刻によるbusy休止・除去・複数surface独立性とする。直接描画とretained/capture、非一様変換とgroup alphaで同じ命令意味を維持し、準備失敗時に旧ツリーを保つ。Automation snapshotとwire clientで値と不定状態を検証する。Windows、Browser、NativeAOT、GPU backendの実行範囲は個別の検証結果で区別し、未実行環境を実測済みとはしない。
