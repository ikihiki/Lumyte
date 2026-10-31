# ADR-UI-0009: Locatorと通常入力で操作するUI自動化

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 8、前提: PR 5・6。積み上げ先はPR 7

## 背景

Compositionで構築したUIを、座標やWidget参照の固定に頼らず検索し、クリック・キー操作・状態検証できるようにしたい。Canvasに描くUIにはDOMがなく、PlaywrightのDOM用Locatorをそのまま使えない。またOnClickを直接呼ぶテストでは、clip、重なり、capture、focus、入力消費を確認できない。

PlaywrightのLocator、操作前の自動待機、再試行するassertionを参考に、Lumyte向けのC#自動操作APIを設ける。Microsoft.PlaywrightとのAPI・プロトコル互換や、ブラウザーDOMへのUI要素公開は対象外とする。

## 決定

### 配置と依存方向

`src/UI/Lumyte.UI.Automation`に任意の.NET 10パッケージを置く。UI・Input.AbstractionsとBCLに依存し、xUnit/NUnit、Microsoft.Playwright、Platform、Diagnostics、Graphicsバックエンドには依存しない。テストは`tests/UI/Lumyte.UI.Automation.Tests`、実ホストへの接続例は既存UIサンプルとBrowserサンプルへ追加する。

UI本体には識別・意味情報、読み取り専用snapshot、通常入力と同じhit testを使うprobeだけを追加する。UIからAutomation実装へ依存せず、Locator、待機、assertionはAutomation側が持つ。自動操作はホストが明示して接続したsurfaceだけを対象とし、Windowや入力sourceを探索・所有しない。

```text
テスト / アプリのスクリプト
    → UiAutomationPage → Locator再評価・自動待機
    → ホストがUI実行箇所へ配送
    → snapshot / pointer probe / UiSurface.Dispatch
    → 通常のhit test・capture・focus・callback
```

### 利用例

```csharp
// hostはusing static Lumyte.UI.Composeで構築したsurfaceを借用する。
// アプリはButton(testId: "save", automationName: "保存", onClick: Save)と
// saved-indicatorを公開し、Saveは部品の状態更新経路を呼ぶ。
var page = new UiAutomationPage(host);
await page.GetByRole(UiRole.Button, name: "保存").ClickAsync();
await page.Expect(page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
await page.GetByTestId("save").PressAsync(Key.Enter);
byte[] png = await page.ScreenshotAsync(); // screenshot対応hostの場合
```

例はSystem、Lumyte.UI、Lumyte.UI.Automation、Input.Abstractions、および`using static Lumyte.UI.Compose`を使用する。この段階ではTextコントロールを含まないため、AutomationNameは明示する。コールバックは[UI-0005](UI-0005-pointer-dispatch-and-capture.md)の構築パラメーターを使う。

### UI本体に追加する公開境界

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。Rect2DとVector2は既存のDrawing2DとSystem.Numericsの型を使う。

```diff
+namespace Lumyte.UI
+{
+    public enum UiRole { None, Group, Button }
+    [Flags]
+    public enum UiAutomationActions { None = 0, Click = 1, Focus = 2 }
+    // 一回のsurfaceへの接続寿命を識別する。0は無効、再利用しない。
+    public readonly record struct UiAutomationId(Guid SurfaceId, ulong Value);
+    public sealed class UiAutomationNode
+    {
+        public UiAutomationId Id { get; }
+        public UiAutomationId? ParentId { get; }
+        public string? TestId { get; }
+        public string? Name { get; }
+        public UiRole Role { get; }
+        // surface論理座標。ClippedBoundsは祖先clipとviewportとの積集合。
+        public Rect2D Bounds { get; }
+        public Rect2D ClippedBounds { get; }
+        // 祖先を含む状態。IsVisible自体は矩形面積や遮蔽を判定しない。
+        public bool IsVisible { get; }
+        public bool IsEnabled { get; }
+        public bool HitTestVisible { get; }
+        public bool IsFocusable { get; }
+        public bool IsFocused { get; }
+        public bool IsPressed { get; }
+        // insideに関係なくpointer/keyのownerを保持しているか。
+        public bool HasActivePress { get; }
+        public UiAutomationActions Actions { get; }
+    }
+    public sealed class UiAutomationSnapshot
+    {
+        public Guid SurfaceId { get; }
+        // snapshot取得ごとの連番。描画frameや状態versionとは区別する。
+        public ulong Sequence { get; }
+        // 通常内容の前順。不変のコピーで、Widget・delegate参照を含まない。
+        public IReadOnlyList<UiAutomationNode> Nodes { get; }
+    }
+    public readonly record struct UiPointerProbe(
+        UiAutomationId? HitElement, UiAutomationId? ActivationTarget);
+    public sealed class UiSurface
+    {
+        // ホストのUI実行箇所から通常更新と逐次実行し、必要なlayoutを確定する。
+        public UiAutomationSnapshot CaptureAutomationSnapshot();
+        // 通常のpointer Downと同じ幾何・clip・Button解決を使う。入力は送らない。
+        public UiPointerProbe ProbePointer(Vector2 logicalPosition);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 意味情報を公開する直接の子。既定は全登録コレクションの子。
+                protected virtual IEnumerable<Widget> GetAutomationChildren();
+                protected virtual UiAutomationActions AutomationActions { get; }
+                protected virtual bool IsAutomationPressed { get; }
+                protected virtual bool HasAutomationActivePress { get; }
+                // 入力を消費する部品は、通常Downと同じ判定を副作用なしで公開する。
+                protected virtual UiInputResult OnProbePointerDown(Vector2 localPosition);
+                // 既定null。nullで解除。空・空白だけの文字列はArgumentException。
+                [ComposeParameter] public string? TestId { get; set; }
+                // 既定null。表示文字列・ARIA nameの自動算出は初期対象外。
+                [ComposeParameter] public string? AutomationName { get; set; }
+                // 既定None。PanelはGroup、ButtonはButtonをコンストラクターで設定。
+                [ComposeParameter] public UiRole Role { get; set; }
+            }
+        }
+    }
+}
```

TestIdの重複はツリー構築時には拒否しない。Locatorのscopeで一意にする。メタデータの宣言setterはlayoutを無効化せず、phase検査や作成threadへの固定を追加しない。通常の構築関数再評価とコンポーネントの専用状態更新経路を使い、UI操作はホスト側で逐次実行する。RoleのEnum.IsDefined検証は行わない。Roleは検索のための意味情報であり、RectangleへButton roleを付けてもClick動作は生成しない。Actionsは部品のAutomationActionsで決め、pressed状態も部品が公開する。基盤がButton型や子プロパティ名で特別扱いしない。

snapshotはGetAutomationChildrenが返す接続中の子を非表示・無効要素も含めて記録する。ButtonはChildrenだけを返し背景を含めない。独自コンポーネントはForegroundやEmbeddeds等の役割に応じて意味情報へ含める子と順序を定義し、基盤はプロパティ名で判断しない。直接の子以外や重複を含む列はInvalidOperationExceptionとし、不完全なsnapshotを公開しない。所有権・接続・解除は、意味情報へ公開しないグループも含む全登録コレクションで管理する。各IDはsurfaceへ接続したときに割り当て、取り外して再接続したら新IDにする。Widgetの値・参照同一性を変えず、古いsnapshotのIDで新しい接続を操作できないようにする。overflowと座標の合成がfiniteでない場合はsnapshotを失敗させる。

### Automationパッケージの公開API

以下は同じ比較元に対する追加API。UiInput、UiModifiers、Key等は既存UI/Inputの型であり、別のキー体系は設けない。

```diff
+namespace Lumyte.UI.Automation
+{
+    public interface IUiAutomationHost
+    {
+        TimeProvider TimeProvider { get; }
+        // actionをホストのUI実行箇所で一回実行。surfaceは外へ持ち出さない。
+        ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action,
+            CancellationToken cancellationToken = default);
+        // 呼び出し後の新しいframeを一回進める。GPU完了は保証しない。
+        ValueTask NextFrameAsync(CancellationToken cancellationToken = default);
+        // host単位で操作入力を排他にし、他の入力と重複しないIDを予約する。
+        ValueTask<IUiAutomationInputLease> AcquireInputAsync(
+            CancellationToken cancellationToken = default);
+    }
+    public interface IUiAutomationInputLease : IAsyncDisposable
+    {
+        long PointerId { get; }
+        // UI実行箇所で送信前に記録し、通常Dispatchへ渡す。
+        // surfaceの所属を確認し、例外でも未解放の入力を保持する。
+        bool Dispatch(UiSurface surface, UiInput input);
+        // DisposeAsyncは自身が送った未完了入力を取り消し、IDと排他を返す。
+    }
+    // PNGを生成できるhostだけが実装する任意の契約。
+    public interface IUiAutomationScreenshotHost
+    {
+        ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed record UiAutomationOptions
+    {
+        // 既定5秒。正でBCL timerが扱える範囲（最大uint.MaxValue-1ミリ秒）。無限待機は初期APIでは提供しない。
+        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
+    }
+    public record UiWaitOptions
+    {
+        // nullはpage既定値。指定値も同じ正のtimer対応範囲。
+        public TimeSpan? Timeout { get; init; }
+    }
+    public sealed record UiClickOptions : UiWaitOptions
+    {
+        // nullはClippedBoundsの中央。指定時は対象Bounds左上からのfinite・非負offset。
+        public Vector2? Position { get; init; }
+    }
+    public sealed class UiAutomationPage
+    {
+        // hostを借用。nullはArgumentNullException、不正optionsはArgumentException。
+        public UiAutomationPage(IUiAutomationHost host, UiAutomationOptions? options = null);
+        public UiLocator GetByTestId(string testId);
+        // nameはordinal完全一致。nullなら名前条件なし。Noneは検索に使わない。
+        public UiLocator GetByRole(UiRole role, string? name = null, bool includeHidden = false);
+        public UiExpect Expect(UiLocator locator);
+        public Task<UiAutomationSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
+        // 非対応hostはNotSupportedException。PNG配列は呼び出し側へ所有権を渡す。
+        public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class UiLocator
+    {
+        // 対象を保持せず、検索条件を追加する。子孫だけを検索する。
+        public UiLocator GetByTestId(string testId);
+        public UiLocator GetByRole(UiRole role, string? name = null, bool includeHidden = false);
+        public Task<int> CountAsync(CancellationToken cancellationToken = default);
+        public Task ClickAsync(UiClickOptions? options = null, CancellationToken cancellationToken = default);
+        // 通常のTab操作でfocusする。直接FocusedWidgetを書き換えない。
+        public Task FocusAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+        public Task PressAsync(Key key, UiModifiers modifiers = UiModifiers.None,
+            UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+    }
+    public sealed class UiExpect
+    {
+        public Task ToBeVisibleAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+        public Task ToBeHiddenAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+        public Task ToBeEnabledAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+        public Task ToBeFocusedAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default);
+        public Task ToHaveCountAsync(int count, UiWaitOptions? options = null,
+            CancellationToken cancellationToken = default);
+    }
+    public enum UiAutomationFailureKind
+    {
+        Timeout, AmbiguousMatch, UnsupportedAction, TargetChanged, ActionFailed, HostUnavailable
+    }
+    public sealed class UiAutomationException : Exception
+    {
+        public UiAutomationFailureKind Kind { get; }
+        public UiAutomationSnapshot? LastSnapshot { get; }
+        // 最大64件。条件・frame・対象ID・待機理由・送信済み入力を保持する。
+        public IReadOnlyList<string> Trace { get; }
+    }
+}
```

page、locator、expectの所属と同時実行は次の規則に従う。

- 自分のhostだけを使う。hostは一つのsurfaceの寿命へ結び付け、surfaceを交換する場合は新しいhost/pageを作る。
- 別pageのLocatorをExpectへ渡すとArgumentExceptionとする。
- 同じpageで待機を含む操作・snapshot・assertionを同時実行するとInvalidOperationExceptionとし、非同期の暗黙並べ替えをしない。
- 別pageは独立に実行できるが、同じhostへの入力はleaseで直列化する。

### Locatorと再試行

LocatorはWidgetやIDを固定せず検索条件を保持し、操作前・待機frameごとに最新snapshotへ再適用する。GetByTestIdは非表示要素も含む完全一致、GetByRoleは既定でIsVisible=falseの要素を除外し、includeHidden=trueなら含める。scopeの子孫は前順で探索し、重なるscopeの結果はIDで重複を除く。

単一要素操作と単一状態assertionはstrictとする。0件は期限まで待ち、複数件はAmbiguousMatchで直ちに失敗する。ToHaveCountとCountは複数を許可する。ToBeHiddenだけは0件も成功とする。Countは一回取得し、ToHaveCountは一致まで再試行する。

VisibleのassertionはIsVisibleかつBoundsの面積が正であることを検査し、遮蔽・画面内への交差は条件にしない。Enabledは祖先を含む状態、FocusedはUI-0006の対象を使う。初期APIはFill/Text/Value、CSS selector、正規表現、強制Click、scroll into viewを提供しない。Text・編集・scrollの導入時に対応する操作を追加する。

待機はhost.TimeProviderの単調時刻で一つの期限を管理し、NextFrameAsyncをawaitして進める。固定Sleepやbusy loopは使わない。UIスレッドをTask.Wait/Resultでブロックしない。headless hostは同じframe契約でlayout・アニメーションと仮想時計を進める。制限時間は内部のfocus取得・lease待機・再検査にも共有し、段階ごとに延長しない。

### Clickのactionabilityと通常入力

Clickは実際のClick capabilityを持つ部品を対象とし、初期の組み込み対応はButtonとする。roleだけを偽装した要素はUnsupportedActionとなる。接続中の一意な要素、可視・有効・HitTestVisible、正のClippedBounds、押下ownerなしを要求する。二つの異なる連続frameでID・Bounds・ClippedBoundsが等しいことを安定条件とする。

既定の操作点はClippedBoundsの中央とする。位置指定はfiniteかつBounds内のoffsetで、その点が実際のclip内にあることも要求する。ProbePointerのActivationTargetが対象部品のIDに一致して初めてクリックできる。内容上のhitから親へ進み、最初にDownを受理する部品のClick capabilityで決める。Buttonは通常DownとOnProbePointerDownで同じinside判定を共有する。中央が遮蔽されていれば待ち、見えている角を自動探索しない。必要なら位置を明示する。

actionability成立後は次の順で操作する。

1. 入力leaseを取得する。
2. UIスレッドの一回の呼び出し内でLocatorを再評価し、ID・幾何・状態・probeを再検査する。変化していたら入力を送らず待機へ戻る。
3. 条件が成立したら同じ呼び出し内で、予約IDによるMove→Down→Upをlease.Dispatchを通して通常のUiSurface.Dispatchへ送る。DownからUpの間にawaitや外部入力の割り込みを入れない。

OnClickの直接実行、privateなcapture表の操作、IsEnabled/IsVisibleの書き換えで成功させない。

後続の入力拡張でButtonより先に入力を消費する部品を追加するときも、probeと通常Downの操作対象判定を共有する。前段で消費される点を親ButtonのActivationTargetとして返さず、入力を送ってから誤判定を補う方式にはしない。

Moveや修飾キーのcallbackが対象・focusを変えた場合は、残りの押下を送らずTargetChangedとし、leaseの清掃へ進む。最初の入力を送った後は、callbackの結果が期待と違っても操作を自動で再送しない。例外もActionFailedとして元例外をInnerExceptionへ保持し、重複実行を防ぐ。Click成功は入力系列の送信完了であり、アプリの非同期処理の完了は後続のExpect等で確認する。OnClick=nullでも正常に入力系列を終えれば成功する。

同期callbackを期限・取消で強制中断しない。送信後に期限へ達した場合も、その入力を再送せず、送信済みであることをTraceへ残す。

### キー操作と後処理

FocusAsyncは最新候補を再評価し、UI-0006の候補条件を満たすことを確認し、入力leaseを取得してからTab Down/Upを送る。既に対象がfocusなら何もしない。各Tab後に再取得し、対象のIDへ到達したら成功する。候補の循環・入れ替わりは同じ期限まで追跡する。focus条件はUI-0006に従うので、0寸法でもfocus候補なら操作できる。

内部のfocus手順は取得済みleaseを借用し、呼び出し元の期限で実行する。Pressや後続の編集操作からpublic FocusAsyncを呼んでleaseを二重取得せず、最初のTabから操作終了・清掃まで同じleaseを保持する。leaseの解放は最外側の操作だけが担当する。

PressAsyncは同じ期限内でこのfocus手順を行い、対象IDとfocusを再検査してから指定キーの非repeat Down/Upを送る。修飾キーの状態と実際の左右修飾キー系列もhostの入力lease内で対応付け、送ったキーは自分で解放する。Enter/Spaceをcallbackへの直接呼び出しで代用しない。

callerの取消はOperationCanceledException（Data["Lumyte.UI.Automation.Trace"]にtrace）、期限切れはTimeoutとして送信済み入力を記録する。開始後の取消・失敗ではleaseが自分のpointerのCancelと未解放キーのUpをUIスレッドへ配送してから排他を返す。後処理はcallerの取消済みtokenを使わず、一度だけ実行する。surface終了で配送不能ならhost側の所有表を解除し、元の失敗に後処理の記録を添える。他の入力をFocusLostで一括解除しない。

キー取消は既存のUiKeyCancelInputを先に送ってownerを発火なしで解除し、必要なUpで消費状態とhostの修飾キー状態の解放を続ける。Upだけを送って、取消したEnter/SpaceからOnClickが発生することを避ける。hostは既存の物理キーが解放された中立状態でleaseを渡し、lease中に外部入力を同じsurfaceへ割り込ませない。終了中のhostは新しいleaseを拒否し、作成済みleaseの後処理を終了処理へ引き継ぐ。UI、Window、InputSystemの所有権をpageへ移譲しない。

Popupの複数surface連携は[UI-0011](UI-0011-popup-windowing-and-fallback.md)で追加する。PopupIdを明示して別pageを選び、任意のrouting host契約で合成入力とprobeを実際のWindow/keyboard scopeへ通す。親画面とPopupは同じ入力leaseを共有する。PR 8単独のdriverは引き続き単一surfaceへ通常Dispatchする。

TextBoxのEdit capability、任意text host、Fill/確定値assertionは[UI-0012](UI-0012-ime-and-text-editing.md)で後から追加する。物理compositionを自動取消せず待ち、通常編集commandを使う。Fillの成功をOS IME試験の代用にしない。

IME候補の専用snapshot・操作・任意hostは[UI-0015](UI-0015-custom-ime-candidate-presentation.md)で追加する。候補操作は現在のcompositionを維持したまま通常の候補controllerを通し、汎用Click/Fillやcallback直呼びでは代用しない。取得元Windowと配送先の対応は注入されたWindow managerで解決する。

拡充するcontrol一覧と導入順は[UI-0013](UI-0013-component-catalog-and-delivery-plan.md)を参照する。新roleだけでClick可能とせず、導入PRでsemantic target・通常入力・probe・focus・閉じた操作planを同時に追加する。仮想itemはkeyによる実体化待ちを設計し、内部templateのpartを独立した操作対象として重複公開しない。

### 実ホスト・スクリーンショット・対応範囲

初期は同一プロセスのC# APIを提供する。headless hostはGPUなしで検索・状態・通常入力を検証し、native/browser hostは実frameを進めて同じAPIを使う。UIレベルのDispatchはOS入力取得やDPI変換を通過しないため、その経路を検証したという扱いにはせず、UI-0007の実入力検証を残す。

ScreenshotHostは新しい描画frameの記録とGPU完了・readbackを待ち、実画素からPNGを返す。通常のNextFrameAsyncの完了をGPU完了とみなさない。キャンセル時の資源保持はUI-0008に従い、透過画像やsnapshotの図示を実スクリーンショットの代わりに返さない。PNG取得時のframeは以前のsnapshotと同一とは限らない。厳密な状態固定は利用側のテストfixtureで行う。

外部プロセスからのDiagnostics経由の操作は、任意の接続として[UI-0010](UI-0010-diagnostics-ui-automation.md)で定義する。この同一プロセスAPIをゲーム側で再利用し、UI本体とUI.Automationへ通信依存を追加しない。JavaScript用client、DOM/ARIA bridge、OSアクセシビリティ、録画・codegen・視覚差分の閾値は別途設計する。初期APIのrole/nameは意味情報の入口であり、OSのアクセシビリティ実装が完成したという主張にはしない。

### 視覚遷移中の操作待機

実装PR 17の[UI-0021](UI-0021-connected-and-presence-transitions.md)でUiAutomationNode.IsTransitioningを追加する。Click/Focus/Pressは先祖/pageを含む演出の入力gateが開くまで通常のframeと共通期限で待ち、IsEnabledやcapabilityの意味を変更しない。snapshotはcommit済みの論理viewだけを扱い、shared-elementのghostをnodeとして公開しない。無関係な演出や他Windowの完了を全体で待たず、操作のために演出を強制停止しない。

## 検討した代替案

- 固定座標だけではlayoutやDPIの変更でテストが壊れやすい。TestIdとrole/nameのLocatorを主な入口にする。
- OnClickを直接実行するとhit test・capture・focus・取消を迂回するため、通常Dispatchを使う。
- Widget参照をLocatorへ固定すると子の置換後に古い要素を操作するため、実行時に再解決する。
- UIをDOMへ二重実装するとnativeとの振る舞いが分かれる。初期は共通UI snapshotを使い、ブラウザーとのbridgeは独立した境界にする。
- 自動操作をUI本体へ内蔵すると待機・host・テスト依存が常時必要になるため、任意パッケージに分離する。

## 結果と影響

Playwrightに近い検索・操作・検証の記述で、同じ入力認識をheadless/native/browserから試せる。通常入力の不具合をテスト用callback実行で隠さず、失敗時には検索条件・待機理由・最後の状態を確認できる。

一方、snapshotのコピーとframeごとの検索コスト、hostのスレッド配送・入力排他の実装が必要になる。初期ClickはButtonに限られ、画面外の要素をscrollして表示する機能もない。Playwright全機能の置換は目標にしない。

## 検証方針

- Locatorのrole/name、TestId、scope、非表示、背景除外、重複と、取り外し・再接続で古いIDを使わないことを確認する。
- 操作可能性について、clip、overflow、前面の遮蔽、0寸法、移動中の二frame安定、owner使用中、lease待機中の置換を確認する。
- 通常入力のMove/Down/UpとTab/Enterの系列、callbackが一度だけ呼ばれること、null callback、例外後の再送なしを確認する。
- 後から出現・有効化する要素の待機、assertion再試行、仮想時計の期限、caller取消、複数一致の即時失敗、同時操作拒否を確認する。
- hostのキャンセル後処理、入力ID競合の防止、surface終了、別page/hostの分離と所有権を確認する。
- native/browserのサンプルで同じ保存ボタンの操作と成功indicatorを検証し、描画対応hostでは実PNGを保存する。OS入力を通すテスト結果と区別する。

## 参考資料

- [Playwright .NET: Locators](https://playwright.dev/dotnet/docs/locators)
- [Playwright .NET: Auto-waiting](https://playwright.dev/dotnet/docs/actionability)
- [Playwright .NET: Assertions](https://playwright.dev/dotnet/docs/test-assertions)

## 実装境界とサンプル

UI本体のsnapshotはCaptureAutomationSnapshotの明示要求時だけ作る。不変コピーを保持するのはAutomationの操作と失敗診断であり、通常Dispatchにsnapshot作成を追加しない。snapshotとprobeはlayoutを確定するが、入力配送自身はgeometryを更新しない既存契約を保つ。

任意のhostはIUiAutomationHostを実装し、入力leaseが自分の送信を記録するDispatchを通す。sampleのUiSampleAutomationHostは専用の自動操作モードだけで使い、その間は外部入力をsurfaceへ渡さない。一般の入力を並行して受けるホストでは、物理キーが中立になるまで予約を待ち、UI配送とleaseの取消不能な清掃を実装する必要がある。

headlessは`--automation record-only`、GPUのoffscreen検証は`--automation vulkan|wgpu`、Native Windowは`native --automation`、Browserは`?automation`で同じ保存操作と成功indicatorを確認する。UiSavedIndicatorは自身の状態更新でInvalidateLayout/InvalidateInputを呼ぶサンプル部品であり、宣言setterに副作用を追加しない。GPUサンプルはcold pipeline準備を含めた30秒の期限を明示し、ライブラリの既定値は5秒のままとする。

ScreenshotHostは新しく記録したsurfaceだけをoffscreenへ描き、GPU完了、readback、PNG符号化後に返す。Window全体や他のHosted surfaceを自動で含めない。取消はGPU使用開始前、または取消なしのGPU完了待機の後に確認する。Nativeはファイル、BrowserはJSホストへbase64を渡せる。DOMの画面キャプチャとは区別する。

Diagnostics接続は次のPRの対象とし、このPRでtransportや遠隔操作を追加しない。実行手順と検証環境の制約は[Automation README](../../../src/UI/Lumyte.UI.Automation/README.md)と各サンプルREADMEに記録する。

### 検証記録

2026-10-29にAutomation 49件（Release／Debug）、既存UI 178件、UI統合32件を検証した。headless保存操作、Vulkan／wgpu（Mesa lavapipe）の実readback PNG保存、PNGのCRCと寸法、成功indicatorの64画素、両backendの厳密画素一致、既存親graphの画素比較を確認した。NuGetだけを参照する外部consumerで静的構築引数・Locator・Expect・probeを確認した。

実Chromiumでは自動操作後にDOM入力へ復帰するsmoke testが成功した。BrowserのGPUモードはInnerExceptionに既知のmapAsyncエラーを保持して失敗し、GPU提示とPNG保存の成功とは扱わない。Native Windowの実操作、trimming／AOTは未検証とし、各READMEの制約を参照する。

### 共通の再構築基盤との接続

再構築で型/keyまたは型/位置が一致する要素はAutomationIdを保持する。旧宣言のIDは切断し、意味情報とcallbackは新しい宣言から取得する。
