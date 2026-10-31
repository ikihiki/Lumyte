# ADR-UI-0021: コネクテッドアニメーションと表示・退出の視覚遷移

- 状態: 採用（実装PR 17）
- 日付: 2026-10-28
- 対応: 実装PR 17、前提: 実装PR 16・UI-0020・UI-0017・UI-0014

## 背景

[UI-0020](UI-0020-animation-and-frame-lifetime.md)は値の補間とTimelineの自動更新を扱う。画面間で同じ対象が移動したように見せるコネクテッドアニメーション、初回表示、遅れて表示される内容、退出、ページ全体の切替には、値の補間だけでなく、新旧の描画、公開時点、入力と資源の寿命を調整する必要がある。

本ADRは視覚的な連続性を扱う。業務状態の更新、navigationのcommit、ページのDisposeを演出callbackへ依存させない。通常のusing staticによる構築とcallback引数を維持し、UIが演出の接続を管理する。

## 決定

### 公式資料の比較と採用する考え方

2026-10-28に以下の公式資料を確認した。各方式の仕組みと、Lumyteで選ぶ方針を区別する。

| 参照先 | 仕組み | Lumyteへの反映 |
| --- | --- | --- |
| [Flutter Hero](https://api.flutter.dev/flutter/widgets/Hero-class.html) | route間の同じtagを対応付け、overlayで飛行させる。遷移先の最初のframeに対象が必要 | 明示keyで対応付け、候補の準備済みlayoutを着地点にする。Widget自体は移動しない |
| [WinUI ConnectedAnimation](https://learn.microsoft.com/en-us/windows/apps/develop/motion/connected-animation) | 遷移元で準備し、遷移先でTryStartする。準備済み情報には有効期限がある | commit前に新旧を準備・検証し、欠落/期限切れなら演出だけを省略する |
| [Motion AnimatePresence](https://motion.dev/docs/react-animate-presence) | 退出が終わるまで要素を保持し、同時開始・退出後開始等を選べる | 退出の視覚資源を保持する。新旧ページを入力可能なまま保持しない |
| [Motion layout/layoutId](https://motion.dev/docs/react-layout-animations) | keyで異なる要素を対応付け、主にtransformで位置・大きさを補間する | layoutを確定してから描画上で補間する。初期版は自動layout変化の全検出を行わない |
| [Flutter AnimatedSwitcher](https://api.flutter.dev/flutter/widgets/AnimatedSwitcher-class.html) | childの切替をcrossfadeし、同type/keyの更新とは区別する | 表示、内容の置換、単なる値更新を別triggerとする。毎frameの再構築を要求しない |
| [WinUI content transitions](https://learn.microsoft.com/en-us/windows/apps/design/motion/content-transition-animations) | 遅れて到着する内容の登場と、同じ領域内の内容置換を扱う | 初回表示・表示状態変更とページ遷移の設定を分ける |
| [Web View Transitions](https://developer.mozilla.org/en-US/docs/Web/API/View_Transition_API/Using) / [skipTransition](https://developer.mozilla.org/en-US/docs/Web/API/ViewTransition/skipTransition) | 新旧snapshotを演出し、演出の省略はview更新の省略にならない | 論理commitと演出完了を分離し、省略でも最終画面を成立させる |
| [MotionConfig](https://motion.dev/docs/react-motion-config) | reduced-motionの全体policyを用意する | host単位のmotion policyを設け、移動の省略をページごとの判断へ分散させない |

Heroの飛行中の行先変更やAnimatedSwitcherの複数退出childの蓄積を、そのまま採用するわけではない。初期版は既存の単一進行navigation・単一親treeを維持し、保持数と処理時間を制限する。通常の表示・退出と共有要素の移動には保持描画layerを使い、旧表示の固定と新旧内容のcrossfadeにsnapshotを限定する。DOM/WinUIの機能は利用せず、既存AnimationとDrawing2Dで全環境共通の契約として実装する。

### 対象と配置

UI本体はkey、表示意図、演出定義とCPU側の参加状態を保持する。UI.Applicationの標準frame coordinatorが保持描画layer、その任意の中間buffer、必要な旧snapshot、提示と退役を所有する。UI.Windowingは既存のpresentation replacementと提示root情報を借用で接続する。Core Signals/Animationへ依存を逆流させず、UI本体からRenderGraph/Window managerへ参照しない。

扱う演出は、要素のenter/exit、ページの同時または退出後のenter、同一navigation slot内の共有要素の位置/サイズ補間とcrossfadeである。一般の並べ替えlayoutアニメーション、scroll連動、spring、dragによるinteractive back、異なるNative Window間の飛行は今回の対象外とする。

### 公開APIの追加差分

比較元はPR 16と先行UI提案。UI-0020のAPIは維持し、追加のruntimeやscopeは公開しない。Vector2はSystem.Numerics、DurationはCore.Time、AnimationEasingはAnimationの既存型である。

```diff
+namespace Lumyte.UI
+{
+    public readonly record struct UiSharedElementKey
+    {
+        // 1〜128文字、制御文字なし。ordinal比較。defaultは無効。
+        public UiSharedElementKey(string value);
+        public string Value { get; }
+    }
+    public sealed record UiVisualPose
+    {
+        public float Opacity { get; init; } = 1f;
+        public Vector2 Offset { get; init; } = Vector2.Zero;
+        // 正かつfinite。原点はcapture矩形の中心。回転は初期対象外。
+        public Vector2 Scale { get; init; } = Vector2.One;
+    }
+    public sealed class UiVisualTransition
+    {
+        // poseを検証してsnapshot。duration/delayは非負。
+        public UiVisualTransition(UiVisualPose from, UiVisualPose to,
+            Duration duration, Duration delay = default,
+            AnimationEasing easing = AnimationEasing.EaseOut);
+        public UiVisualPose From { get; }
+        public UiVisualPose To { get; }
+        public Duration Duration { get; }
+        public Duration Delay { get; }
+        public AnimationEasing Easing { get; }
+    }
+    public sealed record UiElementTransitions
+    {
+        public UiVisualTransition? Enter { get; init; }
+        public UiVisualTransition? Exit { get; init; }
+        // trueなら初回の公開時もEnterする。単なるframe更新では再実行しない。
+        public bool AnimateInitial { get; init; } = true;
+    }
+    public enum UiPresenceState { Hidden, Entering, Visible, Exiting }
+    public sealed class UiAutomationNode
+    {
+        // 先祖/ページを含む視覚遷移の入力gate。通常のIsEnabledと分離する。
+        public bool IsTransitioning { get; }
+    }
+    public enum UiMotionPolicy { Full, Reduced, Disabled }
+    public sealed record UiMotionOptions
+    {
+        public UiMotionPolicy Policy { get; init; } = UiMotionPolicy.Full;
+        // app全体の同時進行数。retiring資源も別途予算へ含める。
+        public int MaximumActiveTransitions { get; init; } = 16;
+        public long MaximumSnapshotPixels { get; init; } = 8 * 1024 * 1024;
+        public long MaximumSnapshotBytes { get; init; } = 64 * 1024 * 1024;
+        // 実時間の上限。Pauseで延長しない。いずれも正。
+        public TimeSpan PreparationTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
+        public TimeSpan MaximumVisualLifetime { get; init; } = TimeSpan.FromSeconds(2);
+    }
+    public static partial class Compose
+    {
+        public static UiVisualTransition FadeIn(Duration duration, Duration delay = default);
+        public static UiVisualTransition FadeOut(Duration duration, Duration delay = default);
+        public static UiVisualTransition SlideIn(Vector2 offset, Duration duration,
+            Duration delay = default);
+        public static UiVisualTransition SlideOut(Vector2 offset, Duration duration,
+            Duration delay = default);
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 構築時のmetadata。一般のIsVisibleは即時の表示制御として維持する。
+                [ComposeParameter] public UiSharedElementKey? SharedElementKey { get; init; }
+                [ComposeParameter] public UiElementTransitions? Transitions { get; init; }
+                // 既定true。Signal引数可。falseは退出後にlayout上も非表示にする。
+                [ComposeParameter] public bool IsPresent { get; set; }
+                public UiPresenceState PresenceState { get; }
+                [ComposeParameter] public Action? OnEntered { get; set; }
+                [ComposeParameter] public Action? OnExited { get; set; }
+                // custom描画は既定false。組込み部品は対応範囲でoverrideする。
+                protected virtual bool SupportsTransitionCapture { get; }
+                // custom描画の状態変更をcapture世代へ反映する。描画cacheは作らない。
+                protected void InvalidateTransitionCapture();
+                protected virtual bool CanReuseTransitionCapture(Widget previous);
+            }
+        }
+    }
+}
+namespace Lumyte.UI.Navigation
+{
+    public enum UiPageTransitionOrder { Together, ExitThenEnter }
+    public sealed record UiPageTransitions
+    {
+        public UiVisualTransition? Enter { get; init; }
+        public UiVisualTransition? Exit { get; init; }
+        public UiPageTransitionOrder Order { get; init; } = UiPageTransitionOrder.Together;
+        public bool ConnectSharedElements { get; init; } = false;
+        public Duration ConnectedDuration { get; init; } = Duration.FromSeconds(0.3);
+        public AnimationEasing ConnectedEasing { get; init; } = AnimationEasing.EaseInOut;
+    }
+    public sealed record UiNavigationOptions
+    {
+        // nullは演出なし。ページの業務状態や履歴には保存しない。
+        public UiPageTransitions? Transitions { get; init; }
+    }
+    public enum UiNavigationPhase
+    {
+        // 既存memberを維持して追加する。commit後、視覚遷移中。
+        Animating
+    }
+    public enum UiVisualTransitionStatus { Completed, Skipped, Interrupted, NotStarted, Failed }
+    public sealed record UiVisualTransitionResult(UiVisualTransitionStatus Status, string Code);
+    // 高度な埋込みhost用の任意能力。通常利用者は実装しない。
+    public interface IUiNavigationVisualChange
+    {
+        ValueTask PrepareVisualsAsync(UiPageTransitions? transitions,
+            Func<bool> skipRequested, CancellationToken cancellationToken);
+        ValueTask<UiVisualTransitionResult> RunVisualsAsync();
+        void StopVisuals();
+    }
+    public sealed class UiNavigationRequest
+    {
+        // ResultのCommittedと別。faulted Taskにせず、視覚結果として完了する。
+        public Task<UiVisualTransitionResult> VisualCompletion { get; }
+        // thread-safeな省略通知。navigation自体の取消や旧pageへの復帰を行わない。
+        public void SkipAnimation();
+    }
+}
+namespace Lumyte.UI.Application
+{
+    public sealed record UiApplicationOptions
+    {
+        // optionsはBuild時に検証・コピーする。
+        public UiMotionOptions Motion { get; init; } = new();
+    }
+}
```

API宣言は既存型への追加memberだけを示す。readonly record structのdefault key、空/制御文字を含むkeyはArgumentException、nullの必須引数はArgumentNullException、不正enum/数値はArgumentOutOfRangeExceptionとして構築/接続時に拒否する。poseのOpacityはfiniteな[0,1]、Offsetはfinite、Scaleはfiniteかつ正とする。duration/delayの負値・加算overflowや不正予算は変更前に例外にする。割当不能等の任意演出の失敗と、アプリが指定した不正な定義を区別する。

FadeInはopacity 0→1、FadeOutは1→0で、他はidentity。SlideInは指定offset→0、SlideOutは0→指定offsetで、opacity/scaleはidentityとする。custom poseの組合せでfade/slide/scaleを同時指定できる。Enterの終点とExitの始点はidentityを要求し、通常描画との境界で突然跳ばせない。初期版の演出は全て有限とし、delayを含む全体の予定時間がMaximumVisualLifetimeを超える定義は準備前に拒否する。

### 通常の静的構築

```csharp
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;

var showDetails = Signal.Create(false);
return Rectangle(width: 160f, height: 100f,
    isPresent: showDetails,
    transitions: new UiElementTransitions {
        Enter = FadeIn(Duration.FromSeconds(0.15)),
        Exit = FadeOut(Duration.FromSeconds(0.1))
    },
    sharedElementKey: new UiSharedElementKey("product:42:image"),
    onEntered: () => LogShown(), onExited: () => LogHidden());
```

LogShown/LogHiddenはアプリの処理とする。遷移元・遷移先では別Widgetを通常Compositionで作り、共有対象だけに同じkeyを付ける。navigationは既存UseNavigationのoptionsへ`new UiPageTransitions { ConnectSharedElements = true, Enter = FadeIn(duration) }`を設定する。WidgetやSignalをroute引数へ入れず、データIDからkeyを作る。keyはAutomation ID、route名、Window IDとは別の表示上の対応情報である。

### 表示・退出のtriggerと状態

IsPresentは既定trueで、同値設定では再生しない。未接続時は意図を保持するだけで購読や通知を開始しない。宣言setterでは状態を遷移させず、再構築の照合またはanimationの専用更新経路で表示値を反映する。接続直後の公開時、または表示値のfalse→trueでEnterを開始する。true→falseはExitを開始し、その間だけ確定済みlayout領域を予約する。Exit完了後は通常の非表示と同様にSize/Boundsを0にし、layoutを無効化する。Exit定義がなければ直ちにHiddenとなる。Hiddenでもtreeの所属とモデルSignalの寿命は保持し、UI-0020の表示producerだけを休止する。

Entering/Exitingでは対象部分木への新規入力・focus・Automation操作を閉じる。退出開始時は既存のcapture/key系列を取消し、IME終了とPopupを通常経路でdrainしてから非操作のlayerを描く。文字入力を中間位置へ配送しない。先祖がHidden/Exitingなら子のEnterを予約せず、先祖がVisibleになるまで待つ。親の初回Enterへ含まれる子の初回Enterは消費し、二重適用を避ける。Hiddenな親の子は公開されるまで待つ。

Enter/Exit中のIsPresent反転は現在のposeから新しい終点へ、変更先のdurationで補間する。中断した側のcallbackは配送しない。最後の世代が所定の状態へ到達し、そのframeが提示された後のIdleでOnEntered/OnExitedを一回配送する。OnExitedはモデルの破棄通知ではなく、callbackを省略してもUIが演出資源を解放する。callbackからの変更は次phaseへ回す。

要素単体の演出は確定layoutの保持描画layerに対し、通常描画の同じpaint順序で変換・group opacityを適用する。page間の共有要素用overlayへ昇格させないため、兄弟や親clipとの前後関係を保つ。Enterでは到達先のlayoutを最初に一度求め、Exitでは最後の提示済みlayoutを保持し、pose補間のたびに再layoutしない。内部モデルの更新は保持layerと必要な中間bufferへ反映し、演出を継続する。初期IsPresent=falseだけではOnExitedを発火しない。

IsVisible=false、明示の子除去、surface Dispose、親ごとの退役は強制非表示で、Exit待ちを追加しない。古い通知を無効化する。退出を見せてから子を除去したい場合だけ、アプリがIsPresent=falseの宣言を返し、OnExitedでモデルを更新して子を除いた宣言を再構築する。Widgetのsetter/子列変更が非同期Disposeへ変わることはない。内容の差替えはtreeの通常transactionであり、同typeの値変更だけではEnterをやり直さない。

### 共有要素の対応付け

keyの探索scopeは一つのnavigation slotに属する新旧ActivationIdの組とする。各activation内でkeyは一意とし、非表示・未生成の要素を探すためのscrollやデータ読込みを自動実行しない。候補の初回準備layoutに存在する、正の面積を持つ可視要素だけが対象となる。重複keyはcandidateの不正定義としてcommit前に失敗させ、対応元/先の欠落はそのpairだけを省略する。

同じkeyの新旧の論理surface座標を、Window managerの提示root座標へ変換して矩形を得る。DPIを二重に掛けず、Hosted原点と既存clipを反映する。同じ物理提示rootと世代で、axis-alignedな有限矩形として表現できる組だけを結ぶ。nested keyでは最も外側のpairだけを使い、内側は独立に飛ばさない。リストへ戻る時も現在生成済みの対象だけを使い、履歴にsnapshotやWidgetを保存しない。

旧・新の要素をそれぞれ画像化し、矩形の位置/サイズをAnimationで補間しながらcrossfadeする。画像の内容/文字の再組版/角丸形状のmorphは行わず、縦横比の変化は画像のscaleとして扱う。文字を含む要素の歪みが不適切ならkeyを画像等の小さな対象へ限定する。元/先の実Widgetは各treeに残し、飛行中だけ通常描画を抑制してlayoutの場所を保つ。

共有要素はページ全体のenter/exit変換から除外し、提示rootのoverlayで一度だけ描く。page背景→page内演出→共有要素→既存Popup/IME候補の順を守り、OSのtop-level overlayを作らない。背景/前景をまたいで自由に飛ばせる保証はせず、描画clipは両endpointのcaptureに焼き込み、飛行中も提示rootのviewportでclipする。通常のPopupや候補の入力順位を変えない。

### 保持描画layerとsnapshotの使い分け

通常のenter/exit、ページのenter、共有要素の移動には、論理座標のDrawList2Dを保持するlayerを使う。poseと描画内容の世代を分離する。再利用可能な部分木では位置・拡大率の変更だけでWidget構築やOnRecordを再実行しない。DrawingCanvas2D.Replay(DrawList2D)で既存の描画commandを現在のtransformとclipへ合成する。元のDrawListとclipは変更しない。この汎用再生機能はDrawing2Dに置き、UI依存やRenderGraph依存を増やさない。

layerは固定画像ではない。Signal等による再構築後もtype/key/slotのpresenceを引き継ぎ、最新Widgetの描画revision・寸法を参照して内容を更新する。内容変更で演出時間やcallback世代をリセットせず、同じpose補間を継続する。通常frame内の録画でユーザー例外が起きた場合は描画を中断する。

不透明な移動・拡大は描画commandの再生だけで実施し、画像を確保しない。半透明なgroupとcrossfade用の新側には、中間bufferへ部分木をまとめてからalphaを一回適用する。子ごとのalpha分配は行わない。変化のない内容はbufferを再利用し、内容変更時には同じ寸法のbufferへ再記録する。寸法変更では新しいbufferを用意し、旧bufferは最後のGPU使用まで保持する。GPU更新は既存render gateで通常描画と直列化し、次の利用可能なGPU frameへ反映する。準備待ちは一layer一件に制限する。opacity 0は描画と画像借用を省略し、opacity 1は直接再生できる。

custom描画も通常のlayerには参加できる。決定的な再記録・資源借用が保証されるSupportsTransitionCapture=trueの部分木は描画commandをcacheし、独自の可変描画状態の変更をInvalidateTransitionCaptureで通知する。既定falseのcustom/TextBox等は必要なframeで再記録し、初回画像へ固定しない。CanReuseTransitionCaptureは再構築時の見た目の一致判定にも利用する。これはUIの宣言setterへ副作用を追加しない。

共有keyの見た目が一致する場合は、新側のlive layerをoverlayで位置・サイズ補間する。対応する通常要素は描画だけを除外し、tree/layout/IsVisibleを変えない。最外側の一致keyを使い、別slot/別presentation rootを接続しない。移動中の新側の内容変更はlive layerへ反映する。着地点の要素がなくなった場合は演出を打ち切る。

見た目が異なる共有要素のcrossfadeと、既に交換した旧ページのexitには、旧側だけを固定snapshotとして保持する。新側は更新可能なlayerであり、画像に固定しない。旧側snapshotを作れない共有要素は移動だけへfallbackする。旧ページ画像を準備できないページ演出は省略する。premultipliedの新旧画像はPlusで一回合成し、半透明crossfadeで背景が混入したりalphaが二重に薄くなったりしないようにする。

旧snapshotには最後に提示したlayout・描画変更世代と一致する内容を使う。不一致なら期限内で新しい通常source frameを待つ。対象部分木を透明背景へ隔離して記録し、画面全体の矩形cropで代用しない。固定snapshotのcaptureはSupportsTransitionCaptureが有効な部分木に限定する。非対応custom描画/backdrop依存を任意能力の不足として扱う一方、OnRecordのユーザー例外は通常描画の失敗として扱う。

標準Applicationは既存Renderer2DとRgba16Floatのsampled/render-attachmentを使う。layer cacheの更新はSampled→ColorAttachment→Sampledを宣言し、通常frame、更新submissionと借用画像の寿命をpresenterが管理する。UI本体とApplication間の内部bridgeは既知のhostへのInternalsVisibleToで接続し、Signalsへのfriend accessは追加しない。

pixel/byte予算は固定snapshotに加えlayerの中間bufferとGPU退役待ち画像も含む。DPI適用後の幅×高さ、Rgba16Float texel payloadの幅×高さ×8をcheckedで合算する。backend allocation paddingやdriver管理領域は既存IGraphicsTextureから取得できないため含めない。移動だけのlayerはpixel/byte予算を消費しない。同時進行数はpage調整一件、各pair一件、各presence一件を数える。予算不足で画質を黙って下げず、任意演出を省略する。

frameは最後のGPU使用とpresentation解放まで画像leaseを保持する。CPU側のlayerが旧bufferやsnapshotの所有を返却しても、提出済みGPU frameから先に解放しない。新旧pageのDI scope、フォント等の借用ownerはGPU drainまで保持する。視覚終了、最終通常frameの提示、GPU資源の退役を別々に管理する。

### navigationのcommitと演出の完了

[UI-0017](UI-0017-navigation-and-page-lifetime.md)のPrepare/Guard/Quiesce/Commit契約は維持し、その前後に視覚資源の準備とphaseを追加する。

1. 旧viewを操作可能に保って候補を準備し、Guardの許可後に両者のkeyを対応付ける。候補を早期公開せず、接続済みSignalのrevisionとviewport/scale/device世代を記録する。
2. 既存の入力・IME・Popup・Diagnosticsのquiesceを進める。候補とsourceがcapture可能な状態で画像を準備する。PreparationTimeoutはcapture開始から測り、pageの業務Prepare/Guard時間へ適用しない。期限切れでは通常の候補準備を使って演出を省略する。
3. 最後のTryValidateCommitでUI-0019のSignalRevision、root/input version、geometryとpresentation/device epochを再検査し、awaitなしで通常の同期commitを行う。旧snapshotのcapture前後のWidget描画revisionとboundsを照合する。公開後の新側の内容変更はlayer更新へ反映する。commit内でcapture、allocation、OnRecordやcallbackを呼ばない。
4. Current/履歴/Target/input routeを新viewへ交換し、Request.CompletionはCommittedになる。新viewのSignalと値アニメーションは公開後のowner更新で再開し、内容をlive layerへ反映する。入力/IMEのgateとは分離する。視覚演出だけをAnimatingとして開始し、新viewの入力gateは閉じておく。ExitThenEnterはsnapshot上の順序であり、旧pageの業務activationを延命して操作可能にするものではない。
5. 最終状態の新viewを通常描画して提示し、geometryの適格性を確認してから入力gateを開く。VisualCompletionをCompletedまたはSkippedとして確定し、その後は通常の旧資源退役へ進む。

ConnectedはTogetherのpage effectと同時に始める。ExitThenEnterでもconnectedはcommit直後から一度だけ始め、EnterはExitの終了後に始める。総時間はconnectedとpage順序の長い側にdelayを加えた時間で検証する。競合する場合の優先順位はconnected→page→elementとする。matched要素の個別Enterを同時に実行せず、shared演出が終わった最終画面へ着地する。page Enterが対象を覆う場合も個別の初回Enterを後から重ねず、その公開triggerを消費してOnEnteredを最終提示後に一回通知する。初期navigationではsourceがないためpage Enterだけが使える。

commit前のCancel/Guard拒否/不正候補ではVisualCompletionはNotStartedとなり、旧viewを復元する。commit後のCancelは既存どおりnavigation結果を変えない。SkipAnimationは準備前なら省略意図を保持し、commit後なら演出を打ち切って新viewの最終状態へ進める。旧pageへ戻さず、入力を開く前の最終提示は省略しない。VisualCompletionはGPU drain完了や旧scope破棄を表さない。

VisualCompletionは要求ごとに必ず一回完了する。codeはcompleted、completed-with-skips、not-configured、no-matches、motion-disabled、motion-reduced、capture-unavailable、budget-exceeded、prepare-timeout、skipped-by-request、presentation-changed、stopped、host-failed、not-committedの閉じた値とする。一部pairの省略後に残りの演出が完了すればCompleted/completed-with-skips、全体の省略はSkipped、開始後の環境変更はInterrupted、通常host障害はFailedとする。設定なしはSkipped/not-configured、connectedのみでpairなしはSkipped/no-matchesとする。公開Taskの結果へkey本文や例外本文を含めない。完了後のSkipAnimationはno-opとする。

同じslotへの次navigationはAnimating/退役中も従来のBusyとする。飛行を別のページへredirectせず、連続要求で旧pageやtextureを無制限に積まない。navigation snapshotへ表示するphaseはAnimating→Retiring→Idleとし、既存のObserve診断でもphaseを観測できる。Request.Completionや診断ClickのleaseをVisualCompletion待ちへ置き換えず、相互待ちを作らない。

### 途中変更、motion policy、環境別fallback

表示演出はUI-0020のactive-time時計を使うが、UI-0021の短い視覚遷移を非表示中に無期限保持しない。Window非表示/最小化/Browser hidden、resize、DPI変化、device loss、対象tree/描画状態の変更、app停止では演出をInterruptedとして最終状態へ進める。古いepochの画像を新しいdeviceへ再利用しない。停止中は最終提示を待たず通知を確定できるが、再表示時は通常frameの適格性を確認してから入力を再開する。

MaximumVisualLifetimeはTimeProviderの実時間で開始から測り、時計休止でも資源保持の上限を進める。完了条件に到達できなければ省略/打切りとして入力gateを最終状態へ進める。ownerがfreeze中の即時清掃やGPU強制完了は保証せず、実行再開後に期限判定と安全なGPU退役を行う。

Reducedでは移動・scale・connected flightを省略し、設定したfadeを最大100msへ短縮する。Disabledでは全視覚遷移を即時状態変更にし、不要なsnapshotを取得しない。Fullでもanimation定義なしなら演出しない。Windows/BrowserのOS設定を読むadapter自体は今回は追加せず、ホスト/利用者がMotion.Policyへ明示する。OS設定へ自動追従済みとは報告しない。policy変更は次回host構築から適用し、途中変更APIは追加しない。

| 条件/環境 | 動作 |
| --- | --- |
| Desktop/Browserの標準renderer | 既存Renderer2Dの描画再生と必要なoffscreen/group合成で描く。DOMのView Transition APIやOS固有のconnected機能を要求しない |
| Native/Hostedの同じnavigation slot | 同じ提示root・世代なら接続する。Popup/別slotと同じkeyでも自動接続しない |
| 別Native Window/別提示root | connectedを省略し、独立したpage enter/exitまたは即時切替にする |
| source/target欠落、未生成/virtualized対象、非対応capture、予算不足 | 欠落pairは省略。旧snapshot非対応なら共有要素の移動だけへfallbackし、必要な中間buffer/旧ページ画像を準備できなければ対応する演出を省略する |
| 標準以外の低水準host、group/layer能力不足 | 表示意図とcallback契約を維持して即時状態へ進む。視覚機能対応を偽らない |
| Headless | 標準Headless hostは視覚演出を省略。ManualClockとfake capture coordinatorで対応/世代/通知/資源退役を検証し、画素の正しさはGPU環境で確認する |

標準hostのないUiSurface単独利用では、演出を即時状態へ進め、次の通常UpdateLayout/Recordを終えたIdleを仮想提示としてcallbackを配送する。この経路はGPU提示を保証しない。実提示後の通知を必要とする埋込みは標準hostのframe coordinatorを利用する。任意の外部hostへcapture/presentation adapterを公開する設計は後続とする。通常ページへ手動pumpやruntimeは要求しない。

任意演出の不足と、通常の候補layout/renderer/device自体の失敗を区別する。capture専用allocationの失敗は省略できるが、通常frameも成立しないdevice lossは既存host復旧/Failure経路へ渡す。commit後に壊れたviewを旧pageへrollbackしない。callback例外は一回だけ記録して既存host停止へ渡し、通知やcommitを再実行しない。

### 入力、IME、Automationと観測

overlay/snapshotはWidget、Automation node、focus、capture、IME clientを持たず、入力を受けない。Animating中の通常入力はgateで受付を止め、演出後に自動再送しない。既存押下・編集はcommit前にdrainする。新IME sessionは新viewの最終提示とfocus適格性の確認後に開始する。

要素のEntering/Exitingも部分木のactionabilityを閉じる。UiAutomationNode.IsTransitioningは先祖とpageのgateを含め、UI-0009の通常操作はfalseと既存Bounds安定条件を同じ期限内で待つ。IsEnabledや操作capabilityを一時的に削ってUnsupportedActionへ変換しない。snapshot取得はcommit済みの論理viewを返し、ghostのkeyや画像を検索対象へ混ぜない。Diagnosticsのsnapshotへは任意bool項目として追加し、既存plan/Operationのversionを変更しない。DiagnosticsのScreenshotは実際の合成結果を許可された通常経路で取得し、内部snapshot textureを別Operationで公開しない。

自動操作を成功させるために演出を勝手に止めず、無関係な要素のEnterや別Windowの遷移は待たない。要素のOnEntered/OnExitedは省略時にも最終状態へ到達後に一回通知するが、途中破棄/停止/旧世代には通知しない。navigationのvisual status/codeは要求元がTaskで観測でき、key本文・画像・編集内容を一般のnavigation診断へ追加しない。

### 宣言的Popup/Windowの終了との接続

2026-10-29追記。PopupのisOpen=false、Window一覧からの除去/isOpen=falseは論理的な終了意図とする。演出を設定したhostでは、最初に対象の新規入力・focus・Automation・IMEを止め、操作の静止後に非操作の視覚資源だけをexit完了まで保持する。表示を継続して入力を受けることで演出を実現しない。演出なし、強制owner喪失、停止、device lossでは演出を省略し、既存drainへ直接進める。

Popup/WindowのonClosedはexitの視覚終了だけでなく、既存のbinding/surface/GPU/診断参加者/実Windowの終了が完了してから通知する。dismiss/onCloseRequestedはモデルへの要求であり、onClosedと混同しない。exit中に同じkeyが再追加されても終了中instanceを復活させず、新しいinstanceは旧清掃後に開始する。これはWindow宣言APIの同key再追加規則を維持する。

enter/exitは同じ提示root内のHostedで実施し、Native Windowの位置/OS opacityをアニメーションするAPIは初期範囲外とする。Nativeでも内容だけのenterを実施できるが、能力不足や非活性表示の制約では即時表示とする。別Window/別navigation slotを同じshared keyで自動接続しない。検証にはdismiss→モデル反映→入力停止→exit→GPU drain→onClosedの順序と、終了中の再表示/停止/owner喪失を含める。

### ページ内Dialogのenter/exit

[UI-0022](UI-0022-declarative-hosted-dialogs.md)のDialogも論理的な開閉と視覚資源の寿命を分ける。初回提示後にenterし、false/宣言除去ではまず入力・IME・子Popupを静止させ、非操作の視覚資源だけをexit完了まで保持する。モーダルのbackdrop/入力遮蔽はexitとGPU退役まで維持し、背景を先に操作可能にしない。操作を再開する直下scopeは清掃完了後に一度選び、終了中Dialogのcallbackから旧focusを復活させない。

onClosedは演出終了だけでなく参加者とGPU等の清掃完了後に通知する。ページ退役/owner喪失/停止/device lossでは演出を省略して通常drainへ進み、終了中の同key再表示は旧session清掃後の新sessionとなる。通常Widgetの明示detach規則を非同期setterへ変えず、Dialog hostが退役資源を所有する。独立した別Dialog/Windowをshared keyだけで自動接続しない。Nativeでも内容内の演出だけを対象とし、OS Windowの位置/opacityを操作する演出や別提示rootへのconnected flightを追加しない。Native modalのowner抑止tokenもexit/GPU/実Window終了まで維持してから返却する。presentation変更では旧演出を中断して通常drainし、新しい方式でのsessionへ視覚資源を転用しない。C8導入時にHosted/Native、animation有無の両方でこの順序を検証する。

## 検討した代替案

- Widgetを旧treeから新treeへ移す方式は単一親とpage/DI寿命を破り、focus/IMEの所有者まで移るため採用しない。
- 旧pageを入力可能なまま退出完了まで残す方式はCurrent/Targetの一括commitを曖昧にするため、旧側は非操作の視覚資源だけを使う。
- 演出失敗でnavigationを取消す方式は任意の見た目が業務遷移を妨げるため採用しない。不正定義の検証と任意演出の不足を分ける。
- Connectedの移動にはlive layerを使う。crossfadeの旧画像は対象部分木を隔離し、Window全体のcropで背景や隣接要素を混入させない。
- 全layout差分の自動FLIPと複数退出childの保持は、保持型treeの既存transactionと入力geometryの変更範囲が広いため、後続の独立した判断とする。

## 結果と影響

画面の対応関係と表示/退出を宣言的に設定でき、論理状態と演出の寿命を分けて扱える。既存46 Widgetの共通metadataとして追加するため、Widget数は増えない。一方、snapshot/合成によるメモリと準備コスト、演出中の入力gate、画像scaleによる見た目の制約が増える。上限とfallbackを実装し、意味のある遷移を演出能力の有無で変えない。実装と検証範囲は末尾に示す。

## 検証方針

- 通常ComposeでSignalのIsPresent、immutableなmetadata、callback lambdaと共有keyを生成でき、runtime/scope/Bindが不要なことをconsumer compilationで確認する。
- ManualClockで初期表示・表示反転・途中反転・同値更新・退出後layout解放・強制detachとcallback一回/旧世代抑制を確認する。
- 同keyの新旧、欠落/重複/nested key、初回frame・virtualized対象、Back/Forward、別slot/Native/Hosted、非一様DPIとclipの座標を検証する。
- Together/ExitThenEnter、pageとconnectedの二重描画抑制、最終提示後の入力/IME開始、通常Automation/Diagnosticsのgate待機と共通期限を確認する。
- Commit前取消、Guard拒否、prepare timeout、source revision不一致、SkipAnimation、Busy、commit後resize/非表示/device loss/停止を検証し、履歴/Current/Targetとvisual結果が混在しないことを確認する。
- 非対応capture、pixel/byte予算、retiring資源の予算算入、group opacity、sRGB/linear premultiplied、背景混入なし、snapshotのGPU完了前解放なしを確認する。
- Full/Reduced/Disabled、capture/通常描画の例外、callback例外とterminal cleanup failure、長時間freeze後の期限処理を確認する。
- 調査したAPIの挙動を実装済みと混同せず、Windows/Browserの画素比較、準備遅延、最大保持memory、連続遷移の退役を実環境で測定する。

## 実装PR 17の境界と受け入れ条件

先行の[PR #94](https://github.com/ikihiki/Lumyte/pull/94)はUI-0020の値/Timeline接続を実装する。本ADRのAPI、視覚遷移、テストとサンプルを同じPR 17に実装する。UI-0020の通常値producerの休止/再開と、短期snapshot演出の打切りを別々に保つ。

1. Widgetの表示意図・key・遷移定義とCPU状態を追加し、型/key/slot照合で引き継ぐ。明示detachではexitのためのWidget treeを残さず、停止時にcallback世代を失効させる。
2. 標準hostへ隔離capture、group合成、準備/実時間の期限、予算、GPU退役を追加する。進行中と退役待ちの両方を同じ予算に含め、すべての合成submissionが終わるまで画像ownerを保持する。
3. UI.Navigationと任意のApplication.NavigationへAnimating/VisualCompletion/SkipAnimationを追加する。論理commitを先に確定し、最終通常frameの提示まで入力/IMEのgateを保ち、画像のdrain後に旧page scopeを退役させる。基本ApplicationはNavigationに依存させない。
4. 同じnavigation slot・presentation rootの外側共有keyを対応付け、pageと共有画像の二重描画を抑制する。別Native Window、欠落pair、非対応captureでは演出だけを省略する。
5. resize、DPI、非表示、Browser freeze、device loss、対象更新、停止で短期演出を最終状態へ進める。Reduced/Disabled、期限超過、予算不足でも通常描画と業務状態を維持する。
6. Automation/Diagnosticsは通常の入力適格性と共通deadlineでgateを待つ。ghostを意味treeへ含めず、表示を観測するために演出を強制完了させない。
7. ManualClockによる世代/反転/取消、GPU画素による背景混入防止とgroup alpha、capture準備/退役中の予算、旧DI scope寿命、Browser/Headless fallback、NativeAOTを検証する。公開stubだけでreadyにしない。UI-0022のDialog部品はC8側で本ADRの寿命へ接続する。

## 実装の適用範囲と検証

標準Applicationの既存Renderer2Dとpresenterへ保持描画layer・中間buffer更新・旧snapshotの経路を追加する。独立した描画エンジン、RenderGraph、利用者向けruntimeは追加しない。custom/TextBox等の通常layerはliveで再記録し、固定snapshotの対応範囲と分ける。同じtype/key/slotの再構築や描画内容変更ではpresenceを保持する。Windowのresize/DPI/device世代変更、非表示、対応keyの消失、停止は短期演出を打ち切る。

最終通常frameの識別は記録時に保存する。合成frameのGPU完了時にclockが終点へ到達していても、そのframeでは入力gateを開かない。onCompletedは演出と旧pageの清掃後に配信し、callbackから次の要求をqueueできる。交換済み旧pageや未公開candidateの破棄で要素exitを再実行しない。

- Drawing2Dの再生テストでtransform/clipの合成、元のcommandの不変性を確認する。
- CoreのManualClockテストでgate、退出layout、反転、世代、detach、fallbackと不正定義を確認する。
- Applicationテストで移動layerの描画command再利用・内容更新・画像確保なし、customのlive記録、GPU wait失敗後のleaseと予算、古い合成frameの完了判定を確認する。
- NavigationテストでHeadless fallback、Disabled、Skipと論理結果・視覚結果、旧scopeの清掃を確認する。
- [Browser sample](../../../samples/Lumyte.UI.Transitions.Browser.Sample/README.md)でfade中のSignal更新、移動中のページ内容更新、group alpha、Hosted Popupのexit、異なる見た目のcrossfade、画像一pixel予算での同じ見た目の共有要素移動、Back/Forward/Skip、入力gateとDI寿命を確認する。
- [Headless sample](../../../samples/Lumyte.UI.Transitions.Sample/README.md)を通常実行とstrict Native AOTで実行する。TestWasm/TestUiPages/TestAotに組み込む。

画素検証はChromium/SwiftShader・240×180で実施する。検証用Hosted managerのWindow準備時計だけを固定し、Motionの期限は実時間で測る。汎用2D shaderによる半透明の大きなbuffer合成にはソフトウェアGPUで負荷があり、実GPUのframe性能や60fpsを保証する測定ではない。Windows実GPUは未検証。C8のDialog部品は本ADRのhost終了経路へ後続で接続する。

## 関連ADR

- [UI-0003: 一方向layout](UI-0003-layout-and-invalidation.md)
- [UI-0004: Canvasへの記録](UI-0004-drawing-and-clipping.md)
- [UI-0008: RenderGraphと資源寿命](UI-0008-render-graph-and-resource-lifetime.md)
- [UI-0009: 通常入力による自動操作](UI-0009-ui-automation.md)
- [UI-0012: IMEと文書編集](UI-0012-ime-and-text-editing.md)
- [UI-0016: 標準host](UI-0016-ui-application-framework.md)
- [GRAPHICS-0022: 2D layerとgroup合成](../graphics/GRAPHICS-0022-2d-render-graph-layers.md)

Enter/Exitやpage遷移の入力gateを変更するときは、Widgetの入力情報を無効化し、通常入力を再開する前にUpdateLayoutで反映する。入力gateを閉じたままの描画pose更新だけでは毎tick入力情報を再計算しない。Dispatchは演出状態のcommit・入力情報の更新を行わず、更新失敗時は既存host停止経路へ渡す。

## GPU待機の共通境界

演出用のoffscreen capture/updateも標準frameと同じMaximumFramesInFlightとsubmitゲートを使う。submit後のGPU完了待ちはゲート外で行い、公開前にdevice世代を確認する。退役した世代の画像や完了を新しい世代へ公開せず、GPU待機・readback・後処理の終了を確認して資源を解放する。
