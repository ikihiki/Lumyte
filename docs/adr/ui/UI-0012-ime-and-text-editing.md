# ADR-UI-0012: IME対応の編集文書とWindows TSF・Browser入力の接続

- 状態: 採用
- 日付: 2026-10-28
- 対応: 実装PR 11、前提: PR 6〜10、GRAPHICS-0024

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。TextBoxの文書・選択・preedit・入力hookをTextBoxElementへ移す。string引数とcallback、TSF / Browser bridgeの契約は維持する。
旧revisionの実行記録は履歴として扱い、以下のElement分離後の再検証記録を参照する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

キーコードから文字を合成すると、日本語IMEの変換、再変換、選択範囲の置換、音声入力等を正しく扱えない。WindowsではText Services Framework（TSF）が文書の読み書き、選択、変換範囲、文字位置を問い合わせる。Browserでもcompositionイベントだけでは確定文字の重複や削除・貼り付けを処理できない。

WindowsはTSFの`ITfThreadMgr`を中心とする実装を採る。現行WindowsPlatformはSTAでWindowとキー入力を扱うが、TSF文書storeも文字入力も提供していない。既存キー入力へ文字列を継ぎ足すのではなく、文書編集と物理キーを区別する必要がある。

[UI-0011](UI-0011-popup-windowing-and-fallback.md)のMenu scopeはEnter/Escape/矢印を使う。IMEが消費したキーをそこへ再配送すると、確定と同時にButtonを押す、変換取消と同時にメニューを閉じる等の不具合になる。またHostedや非活性Native popupでは、OSのfocus Windowと文字を描くWindowが異なり得る。

## 決定

### 範囲と依存方向

単一行のTextBox、共通の編集文書、Windows TSF、Browser textarea adapter、headlessテスト、Automation/Diagnosticsによる確定文字列の編集を実装PR 11へ追加する。CompositionでTextBoxを構築し、callbackは名前付きパラメーターで渡す。候補はSystem表示を既定とし、任意の独自描画・候補操作は[UI-0015](UI-0015-custom-ime-candidate-presentation.md)で定義する。一般のText表示、複数行、rich text、undo/redo、password入力、LinuxのIBus/Fcitx接続は別途設計する。

新規packageの依存は次の方向とする。

```text
src/Core/Lumyte.TextEditing                    BCLのみの編集文書・transaction
src/Platform/Lumyte.Platform.TextInput.Abstractions
    → TextEditing + Windowing.Abstractions + Input.Abstractions
src/Platform/Lumyte.Platform.Windows.TextInput
    → Windows platform + TextInput.Abstractions（TSF COM）
src/Platform/Lumyte.Platform.Browser.TextInput
    → Browser platform + TextInput.Abstractions（DOM）
src/UI/Lumyte.UI.TextEditing
    → UI + TextEditing + Graphics.Text（TextBoxと文字layout契約）
src/UI/Lumyte.UI.TextEditing.Parley
    → UI.TextEditing + Graphics.Text.Parley（caret対応provider）
src/UI/Lumyte.UI.Windowing.TextInput
    → UI.Windowing + UI.TextEditing + TextInput.Abstractions + Platform.Abstractions（focus・座標・入力）
src/UI/Lumyte.UI.TextEditing.Automation
    → UI.TextEditing + UI.Automation（通常編集commandのadapter）
```

各assembly・名前空間・NuGet名はdirectory名と同名とする。UI本体からPlatform/COM/DOM/文字組みbackendを参照しない。既存InputRecordのunionへIME文字列を追加せず、出所・session世代付きの編集経路を別に持つ。キーと編集の順序は取得側で確定し、後から現在のfocusで割り当てない。

### 文書と変換中の状態

文書は一つの実行系列上の権威とし、TSF用の非同期shadow文書とUI文書を別々に更新しない。`TextDocumentSnapshot.Text`はpreeditを含む作業文字列、`CommittedText`は未確定の編集を除くアプリ向け文字列とする。

composition中の文書は次の規則で更新する。

- 開始時の文字列・選択を保存し、更新は同じcomposition範囲を置換する。変換中の文字を単純に末尾追加しない。
- 部分確定でcompositionから外れた編集は確定文書へ昇格し、rollback checkpointを更新する。
- commit時に残る編集を確定する。明示cancel時は未確定部分だけを最新checkpointへ戻す。

Revisionは文字、選択、composition属性を含む原子的な変更ごとに増える。snapshotは不変のコピーで、UIが読んだrevisionとgeometryのrevisionを対応させる。TSF lock中は文書だけを操作でき、Widget変更・layout・アプリcallbackは呼ばない。文書の変更通知はqueueへ集約し、通常frameのIdleで描画無効化とcallbackへ反映する。

Windows ACP、.NET string、DOM selectionはUTF-16 code unitで統一し、範囲は半開区間`[Start, Start + Length)`、選択はAnchor/Activeと方向を保持する。UTF-8のglyph cluster indexをACPとして使わない。公開編集の位置はsurrogate pairを分断せず、keyboardの左右移動・Backspace/Deleteはextended grapheme cluster境界を使う。IMEの属性範囲は文書範囲として保持し、描画のcluster対応と区別する。NFC等の暗黙の正規化は行わない。

単一行文書は改行を空白へ正規化する。CRLFを一つの空白、CR/LFをそれぞれ一つの空白とし、置換後のUTF-16位置で選択・変更範囲を計算する。入力長は既定32768 code unit、composition属性は最大256範囲とし、上限超過は切り捨てずtransaction開始前に拒否する。TSFにも同じ制約をQueryInsert等で報告する。

文字列・list・geometryは受付時に検証してコピーする。範囲外位置、負の長さ、未知enum、非finite座標、負寸法を変更前に拒否する。revision/世代のoverflowも開始前に失敗させる。取得queueは既定4096件に制限し、欠落時はsessionを取消して再同期する。編集を一部だけ破棄したまま成功させない。

### 共通文書とUIの公開API

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。UI側はUI-0002〜0011のAPIを前提とする。Widgetは`Lumyte.UI.Compose.Definitions.Widget`の略記とする。

```diff
+namespace Lumyte.TextEditing
+{
+    public readonly record struct TextSpan(int Start, int Length);
+    public enum TextCaretAffinity { Leading, Trailing }
+    public readonly record struct TextSelection(int Anchor, int Active,
+        TextCaretAffinity AnchorAffinity = TextCaretAffinity.Leading,
+        TextCaretAffinity ActiveAffinity = TextCaretAffinity.Leading);
+    public enum TextEditOrigin { Platform, Keyboard, Clipboard, Application, Automation }
+    public enum TextCompositionEnd { Commit, Cancel }
+    public enum TextUnderline { None, Solid, Dotted, Thick }
+    public sealed record TextCompositionSpan(TextSpan Range, TextUnderline Underline, bool Selected);
+    public sealed record TextCompositionSnapshot(Guid Id, TextSpan Range,
+        IReadOnlyList<TextCompositionSpan> Spans);
+    public sealed record TextDocumentSnapshot(ulong Revision, string Text, string CommittedText,
+        TextSelection Selection, TextCompositionSnapshot? Composition);
+    public sealed record TextDocumentChange(ulong Revision, TextEditOrigin Origin,
+        bool CommittedTextChanged, bool SelectionChanged, bool CompositionChanged);
+    public enum TextNavigation { Previous, Next, Start, End }
+    public interface ITextDocumentNavigator
+    {
+        // Widget/GPUへ触れないCPU処理。最新文書のvisual caret位置を返す。
+        TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend);
+    }
+    public sealed class TextDocument
+    {
+        // 呼び出しを直列化する。initialTextは非null。上限は1〜1048576。
+        public TextDocument(string initialText = "", int maximumLength = 32768);
+        public TextDocumentSnapshot CaptureSnapshot();
+        // backend/editor用。排他的なtransaction。再入拒否、Disposeで未commit分をrollback。
+        public TextDocumentTransaction BeginEdit(TextEditOrigin origin);
+        // composition/編集中はInvalidOperationException。中断・取消後に呼ぶ。
+        // expectedRevisionが違えば変更せずfalse。範囲・Unicode・上限もcommit前検査。
+        public bool TryReplace(ulong expectedRevision, TextSpan range, string text,
+            TextSelection selection, TextEditOrigin origin);
+        public bool TrySetSelection(ulong expectedRevision, TextSelection selection,
+            TextEditOrigin origin);
+        // 内部のplatform transactionで蓄積した変更を一度だけ取得。callbackは持たない。
+        public IReadOnlyList<TextDocumentChange> DrainChanges();
+    }
+    public sealed class TextDocumentTransaction : IDisposable
+    {
+        // 各操作を変更前に検査。composition中のApplication/Automation編集は拒否。
+        public void Replace(TextSpan range, string text, TextSelection selection);
+        public void SetSelection(TextSelection selection);
+        // nullで未確定範囲を終了。範囲の縮小に伴う部分確定もここで正規化する。
+        public void SetComposition(TextCompositionSnapshot? composition);
+        public void EndComposition(TextCompositionEnd behavior);
+        public void Commit();
+        public void Dispose();
+    }
+}
+namespace Lumyte.UI
+{
+    public sealed class UiSurface
+    {
+        // Idle専用。UI-0005と同じ最前面・祖先clip・背景除外。背面へ透過しない。
+        public UiElement? HitTest(Vector2 logicalPosition);
+        // 同surfaceの通常内容かnull。UI-0006の適格性で判定。別所属はArgumentException。
+        // 成功時は旧key ownerをClickなしで取消。非適格ならfalseで変更なし。
+        // TryFocus(UiElement?)はUI-0006で実装済みの共通APIを使用。
+    }
+}
+namespace Lumyte.UI.TextEditing
+{
+    public sealed record UiTextChange(string Text, ulong Revision, TextEditOrigin Origin);
+    public static class UiTextHitTesting
+    {
+        // owner threadのIdle。最新の確定layoutから最寄りTextBox/Buttonを判定する。
+        // TextBoxが先ならその参照、Buttonが先/対象なしならnull。focus/文書を変更しない。
+        public static TextBoxElement? ResolveEditor(UiSurface surface,
+            Vector2 logicalPosition);
+    }
+    public class TextBoxElement : UiElement<Compose.Definitions.TextBox>, ITextDocumentNavigator
+    {
+        // mount後の低水準アクセス。通常の構築ではstringとcallbackだけを使う。
+        public TextDocument Document { get; }
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // このassemblyの生成factoryはLumyte.UI.TextEditing.Compose.TextBox。
+            [Composable]
+            public partial class TextBox : Lumyte.UI.Compose.Definitions.Widget
+            {
+                // Focusable=true、Role=TextBox。文書・選択・preeditは論理editorが所有し、接続中の編集ownerは一つ。
+                public TextBox();
+                // 入力はTextBoxElement.OnInputで受け、下記handlerへ委譲する。サービスは借用。
+                [ComposeParameter] public string Text { get; set; } = string.Empty;
+                [ComposeParameter] public IUiTextLayoutProvider? LayoutProvider { get; set; }
+                [ComposeParameter] public bool IsReadOnly { get; set; }
+                [ComposeParameter] public Action<UiTextChange>? OnTextChanged { get; set; }
+                [ComposeParameter] public Action<TextCompositionSnapshot?>? OnCompositionChanged { get; set; }
+            }
+        }
+    }
+    // UI.TextEditingに定義し、UI.Windowing/Platformへ依存しない。
+    public interface IUiTextInputHandler
+    {
+        UiInputResult HandleInput(TextBoxElement editor, in UiInputContext context);
+        void OnPointerCaptureChanged(TextBoxElement editor, UiPointerCaptureState state);
+    }
+    // snapshot内のUTF-16位置とvisual caret/selectionを同じshaping結果へ対応させる。
+    public interface IUiTextLayoutProvider
+    {
+        UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth);
+    }
+    public readonly record struct UiTextCaret(int Position, TextCaretAffinity Affinity);
+    public abstract class UiTextLayout
+    {
+        public abstract ulong DocumentRevision { get; }
+        public abstract Vector2 Size { get; }
+        public abstract TextLayout2D Drawing { get; }
+        public abstract IReadOnlyList<UiTextCaret> CaretStops { get; }
+        public abstract Rect2D GetCaretBounds(UiTextCaret caret);
+        public abstract IReadOnlyList<Rect2D> GetRangeBounds(TextSpan range);
+        public abstract UiTextCaret HitTest(Vector2 position);
+        // bidiのvisual順とgrapheme境界を使い、UTF-16 indexへ戻す。
+        public abstract UiTextCaret MoveCaret(UiTextCaret caret, bool forward);
+    }
+}
```

TextBoxはTextから内部文書を作成する。LayoutProviderがnullなら編集・文字描画を行わず、後から有効なproviderを持つ宣言へ更新できる。Surfaceの入力scopeとの接続は維持し、共通HitTestで対象となる位置では編集不可を外側Buttonへの透過理由にしない。型/keyによるeditor寿命の変更、IsReadOnly、有効性、所属の変更は古いsessionの終了を要求する。ReadOnlyは選択とcopyを許すが置換・compositionを拒否する。

OnTextChangedは入力・clipboard・AutomationによってCommittedTextが変化したときだけframeの変更反映時に呼ぶ。Text引数からのApplication更新と、それに上書きされた古いrevisionを通知対象にせず、モデルへechoしない。OnCompositionChangedは開始・更新でsnapshot、終了でnullを渡す。複数revisionが一frameにある場合は最終状態へ集約し、変更があった項目だけをTextChanged→CompositionChangedの順に通知する。

delegateの通知直前の取得、null、省略、代入による置換は[UI-0005](UI-0005-pointer-dispatch-and-capture.md)のcallback規則に従う。callback例外でも既に成立した編集を再送・rollbackせず、残りの清掃後にhostへ報告する。

Windowsのcopy/cut/pasteは任意の借用IClipboardへ委譲する。非同期read完了時にsession/generation/revisionを再検査し、変化していれば貼り付けを取消す。read待ちの間の入力を停止せず、別editorへ結果を適用しない。BrowserのDOM pasteを採用した同じ操作に、managedのclipboard readを重ねない。

TextBoxは[UI-0003](UI-0003-layout-and-invalidation.md)の一方向layoutに従い、最終幅制約からDrawings・selection背景・preedit下線・caretを同じlayoutで記録する。無限max幅では文字の有限advanceを使い、有限max幅ではその制約内の幅を返す。単一行のoverflowはcontrol内で水平表示位置を変え、caretを可視にする。この内部offsetは一般scrollコンテナーとは別に扱う。

[GRAPHICS-0024](../graphics/GRAPHICS-0024-text-rendering.md)の共通TextとParleyを利用するが、既存glyph cluster情報だけをcaret API完成とみなさない。このPRでParley providerと必要なRust shimのcursor/selection結果を追加し、UTF-8/UTF-16対応表、結合文字、合字、bidiのvisual caret stopを検証する。glyphのadvanceを文字数で均等分割しない。フォントはホストが明示供給する。

```diff
+namespace Lumyte.UI.TextEditing.Parley
+{
+    public sealed class ParleyUiTextLayoutProvider : IUiTextLayoutProvider
+    {
+        // engineは借用し、接続と同じthreadで使う。optionsはコピーする。
+        public ParleyUiTextLayoutProvider(ParleyTextEngine2D engine, ParleyTextOptions2D? options = null);
+        public UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth);
+    }
+}
```

例えば次のように文字列とcallbackを構築パラメーターで渡す。engineには利用側がフォントを登録する。

```csharp
string text = "初期値";
var provider = new ParleyUiTextLayoutProvider(engine);
var editor = Lumyte.UI.TextEditing.Compose.TextBox(
    text: text,
    layoutProvider: provider,
    onTextChanged: change => text = change.Text);
```

### Windowとsessionの公開境界

```diff
+namespace Lumyte.Platform.TextInput.Abstractions
+{
+    [Flags]
+    public enum TextInputFeatures { None = 0, CommittedText = 1, Composition = 2, Geometry = 4 }
+    public sealed record TextInputTarget(WindowId FocusWindow, WindowId PresentationWindow,
+        Guid SurfaceId, Guid EditorId);
+    public sealed record TextInputSessionStatus(Guid SessionId, ulong Generation,
+        bool IsActive, bool IsComposing, string Code);
+    public sealed record TextInputRangeGeometry(TextSpan Range, WindowRect Bounds);
+    public sealed record TextInputCaretGeometry(int Position, TextCaretAffinity Affinity, WindowRect Bounds);
+    public sealed record TextInputGeometry(ulong DocumentRevision, ulong LayoutRevision,
+        WindowRect Viewport, WindowRect Caret, IReadOnlyList<TextInputRangeGeometry> Ranges,
+        IReadOnlyList<TextInputCaretGeometry> CaretStops);
+    // IME未消費だが編集scopeが所有するキー。InputSystemへ二重配信しない。
+    public sealed record TextInputKey(InputWindowId Origin, InputWindowId StateWindowId,
+        InputCaptureStamp Capture, Guid SessionId, ulong Generation,
+        ulong Sequence, Key Key, bool IsDown, bool IsRepeat,
+        bool Shift, bool Control, bool Alt, bool Meta);
+    public interface ITextInputService : IDisposable
+    {
+        TextInputFeatures Features { get; }
+        // 同一owner threadの文書を借用。一つの実keyboard scopeにactive session一つ。
+        // 未対応はNotSupportedException。戻る前にfocus対応を検査する。
+        ITextInputSession Begin(TextInputTarget target, TextDocument document,
+            ITextDocumentNavigator navigator);
+        // platform Pump後・UI処理前。完了/取消/文書通知を進め、同期waitしない。
+        void Advance();
+        // 取得時に固定したsessionへの編集キー。sequence順、一回だけ排出。
+        IReadOnlyList<TextInputKey> DrainKeys();
+    }
+    public interface ITextInputSession : IDisposable
+    {
+        TextInputSessionStatus Status { get; }
+        // geometryはPresentationWindowのclient論理座標。配列はコピーする。
+        void UpdateGeometry(TextInputGeometry geometry);
+        // owner threadでqueue。IME終了と文書commit/restore完了までAdvanceを続ける。
+        void RequestEnd(TextCompositionEnd behavior);
+        bool IsReleased { get; }
+        // Cancel終了を要求するだけ。IsReleasedまで文書/Windowは保持する。
+        void Dispose();
+    }
+}
+namespace Lumyte.UI.Windowing
+{
+    public sealed class UiPopupManager
+    {
+        // Idle専用。実配送と同じWindow/surface/clip/scope・Widget入力順で判定し、入力は送らない。
+        // 編集targetがDownを所有する位置ではHitElementを保ちActivationTarget=nullとする。
+        public UiPointerProbe ProbePointer(UiSurface surface, Vector2 logicalPosition);
+        // Menuを開く前の非同期barrier。戻ったtokenをMenu終了まで保持する。
+        // 取消・失敗でも取得済みtokenを回収する。owner threadで開始/commit。
+        public IDisposable RegisterMenuGuard(UiPopupOwner owner,
+            Func<CancellationToken, ValueTask<IAsyncDisposable>> prepare);
+    }
+}
+namespace Lumyte.UI.Windowing.TextInput
+{
+    public interface IUiTextInputTargetResolver
+    {
+        // owner thread。manager ID・生存・SourceIdと実Windowの対応を検査する。
+        // 解決できなければNotSupportedException。現在activeのWindowで補完しない。
+        TextInputTarget Resolve(UiWindowId focusWindow, UiWindowId presentationWindow,
+            Guid surfaceId, Guid editorId);
+    }
+    public sealed class UiPlatformTextInputTargetResolver : IUiTextInputTargetResolver
+    {
+        // 全て借用。resolveNativeはSourceIdを明示登録したIWindowへ解決する。
+        public UiPlatformTextInputTargetResolver(IUiWindowManager windows,
+            IWindowInputSource input, Func<Guid, IWindow?> resolveNative);
+        public TextInputTarget Resolve(UiWindowId focusWindow, UiWindowId presentationWindow,
+            Guid surfaceId, Guid editorId);
+    }
+    public sealed class UiTextInputConnection : IUiTextInputHandler, IDisposable
+    {
+        // 全て借用。windowは同manager内でsurfaceを提示するwindow。
+        public UiTextInputConnection(UiSurface surface, IUiWindowManager windows,
+            UiWindowId window, ITextInputService service, IUiTextInputTargetResolver targets,
+            IClipboard? clipboard = null);
+        // TextBox.OnInputからだけ呼ぶ。surfaceで再hit test・再Dispatchしない。
+        public UiInputResult HandleInput(Lumyte.UI.TextEditing.Compose.Definitions.TextBox editor,
+            in UiInputContext context);
+        public void OnPointerCaptureChanged(Lumyte.UI.TextEditing.Compose.Definitions.TextBox editor,
+            UiPointerCaptureState state);
+        public void Reset();
+        // 入力境界の全surface Idleでfocus/sessionだけを同期。layout/文書callbackは実行しない。
+        public void SynchronizeFocus();
+        // Input/Popup配送後にfocus・変更・callbackを同期する。全surfaceのIdleで呼ぶ。
+        public void Advance();
+        // layout成功後に一frame一回。現revisionの幾何をserviceへ公開する。
+        public void PublishGeometry();
+        // current sessionのCancel終了を待ち、token解放まで再開を抑止する。
+        public ValueTask<IAsyncDisposable> SuspendAsync(CancellationToken cancellationToken = default);
+        // 明示Commit用の同じbarrier。owner threadで開始し、文書確定・session解放後に返す。
+        public ValueTask<IAsyncDisposable> SuspendAsync(TextCompositionEnd behavior,
+            CancellationToken cancellationToken = default);
+        public void BeginStop();
+        public bool IsStopped { get; }
+        // IsStopped後のみ。借用service/surfaceを破棄しない。
+        public void Dispose();
+    }
+}
+namespace Lumyte.Platform.Windows.TextInput
+{
+    public sealed class WindowsTextInputService : ITextInputService
+    {
+        // WindowsPlatformと同じSTA。platformを借用し、message filter登録を所有する。
+        public WindowsTextInputService(WindowsPlatform platform);
+    }
+}
+namespace Lumyte.Platform.Browser.TextInput
+{
+    public sealed class BrowserTextInputService : ITextInputService
+    {
+        // BrowserPlatformと同じJS context。proxy用textareaとlistenerだけを所有する。
+        public static ValueTask<BrowserTextInputService> CreateAsync(BrowserPlatform platform,
+            string modulePath = "./lumyte-text-input.js");
+    }
+}
```

UI側のWindow対応は[UI-0014](UI-0014-injectable-window-manager.md)のsnapshotで決める。connectionは対象windowのKeyboardRootとPresentationRootをresolverへ渡す。Hostedなら描画root、Native popupなら実際のpopupがPresentationRootとなり、PreserveFocusでもKeyboardRootは編集元の登録rootを保つ。resolverだけがSourceIdからPlatformのFocusWindow/PresentationWindowへ変換し、IWindowInputSourceのInputWindowId対応とも照合する。WindowSystem adapterではTryResolveNativeWindowを利用でき、独自managerは独自resolverとserviceを注入できる。UI.Windowingの汎用境界へIWindow/WindowNodeを持ち込まない。EditorIdとSessionIdは再接続ごとに新規発行し、Generation・Revisionと合わせて古い非同期結果を拒否する。surface/Widgetの参照をOS callbackへ持ち込まない。

pointerはUiSurfaceの共通hit testからOnInputで配送し、TextBox自身がInputHandlerへ委譲する。Downでcontext.RequestFocusとCapturePointerを使い、context.PointerPositionから同じlayoutのcaretを求める。UiTextHitTesting.ResolveEditorはAutomation/probe用の副作用のない観測に限定し、実入力の配送先を別に選び直さない。Button内のTextBoxは内側の編集targetが消費し、外側Buttonを押さない。selection dragはmanagerのWindow/surface captureとUiSurfaceのElement captureを使い、終端まで系列を維持する。別surfaceへドラッグを受け渡さない。

入力handlerはTextBoxの構築引数・プロパティへ渡さず、ホストがSurfaceごとに一度接続する。UiTextInputConnectionは作成時にUiTextInputScope.Attach(surface, this)を登録し、drain後のDisposeで解除する。同一Surfaceへの二重登録は拒否する。TextBoxElementはUiElement.Surfaceから所属を取得し、そのSurfaceのhandlerを自動解決する。登録はConditionalWeakTableでSurfaceへ対応付け、process-globalなcurrent serviceを設けない。切断後や別Surfaceへの再接続では旧handlerを使わない。UIアプリhostはDIからbackendを解決してWindow/surface scopeでconnectionを所有し、各ページ・TextBoxへbackendを繰り返し指定させない。独自host向けにはUiTextInputScope.Attach(UiSurface, IUiTextInputHandler)の登録leaseを公開する。共通UiSurfaceにTextBox判定やsurface手前の編集用adapterを追加しない。TextBoxはOnInputとOnPointerCaptureChangedで委譲し、同じfocus/key/pointer系列の取消を受ける。handler未設定・LayoutProvider未設定・ReadOnlyでも、編集target上のpointerやactivation keyを外側Buttonへ透過させない。未設定で編集できない場合もTextBox側がHandledで止める。

UI-0011のrouting hostはmanager.ProbePointerへ委譲し、次の結果を返す。

- Window/surfaceが実配送先でなければ空のprobeとする。
- TextBoxがDownを所有すれば通常probeのHitElementを保持してActivationTargetをnullにし、外側Buttonを返さない。

headlessのTextEditing.Automation hostも同じ規則を使う。UI.Automationは既存のIUiAutomationRoutingHostだけを参照し、UI.Windowingへ依存しない。実入力・Probe・Clickの直前検査で同じ確定layoutとresolverを使用する。

配送優先度はOS/DOMのIME→既存系列の取消・消費→popup scopeによるsurface選択→通常UiSurface.Dispatch（TextBox/各WidgetのOnInput→未処理時のmenu navigation）→アプリとする。TextBoxのHome/End/左右/編集commandをmenuが先取りしない。IMEが消費したDown/UpはゲームやUIへ流さず、物理終端まで消費表を保持する。既にUIへ渡したDownのUpだけをIMEが消費した場合はUiKeyCancelInputで旧ownerを先に取消す。platform内部の物理キー状態は常に追跡する。一方InputSystemの論理キー状態には、UI/ゲームへ公開した系列だけを入れ、IME消費済み入力をアクションへ流さない。

文書の通常編集commandと文字確定は取得順に一つずつ文書へ適用し、次の文字の前にcaret移動を成立させる。WindowsのfilterはWidgetを呼ばず、共有編集commandとITextDocumentNavigatorのCPU処理でこれを行う。BrowserはDOMの実際の編集順を使う。TextInputKeyへqueueするのは未消費のTab等、UI scopeへ渡す必要のあるキーで、通常Input sourceへ二重登録しない。connectionは一回だけUiKeyInputへ変換する。Begin時に既にInputSystemへ公開されていた押下は次のKeyCanceledDataで取消し、対応する物理Upまで新scopeへ押し直さない。

TextInputKeyのOriginは取得時点、StateWindowIdは系列のownerを示し、[INPUT-0007](../input/INPUT-0007-window-associated-input.md)のInputWindowIdを使う。Captureは通常sourceと同じ取得counterから消費判定前に受け取る因果ID、Sequenceはsession内の一回配送用連番とする。取得adapterは通常入力と編集scopeキーを元の取得列の配送位置へ一度だけ渡す。ホストは二つのqueueを単純連結したりCaptureで再sortしたりせず、sourceの取得順と同じ取得から派生したrecordの順を保つ。

```diff
+namespace Lumyte.Input.Abstractions
+{
+    // 追加する論理系列の取消。通常のUp/activationを生成しない。
+    public sealed record KeyCanceledData(Key Key) : InputData;
+}
```

InputSystemはKeyCanceledDataで当該キーの論理押下を解除し、その取消を記録する。Actionsの認識器はこの記録で関連認識を発火なしで中断し、host routerはUiKeyCancelInputへ変換する。Recorded通知とGetRecords/ReadRecordsの履歴ではInputRecord.Dataに取消型をそのまま残し、通常Upへ置換しない。Input.Diagnosticsの状態snapshotとlatestSequenceも取消後の論理状態・履歴と整合させ、新しいwire encoderや操作commandは追加しない。消費済みUpだけが後からゲームへ届く抜け道を作らない。この追加も実装PR 11の範囲とし、既存Input ADRのkey型と併用する。

focusを変え得る入力にはTabだけでなく、mouse/touch/penのDown、Buttonのcallbackを起こすUp等も含める。文字編集を取得側で先行適用する前に、次の共通境界を通す。入力のOrigin、sequence、既存系列の所有先は取得時に固定し、後のactive Windowから補完しない。

```text
focus変更の可能性がある入力を取得
  → 通常routerへ一度配送 → 全surfaceがIdleへ戻る
  → SynchronizeFocus（旧sessionの終了 → 解放確認 → 新sessionの開始）
  → 次の新しい文字/編集入力を取得・適用
```

Windows pumpは該当MSGをqueueしたところで通常入力のdrainをyieldする。ホストがWindow/Input更新・配送・SynchronizeFocusを行ってから次のMSGへ進み、filter/TSF lock内からWidgetを呼ばない。Browserでは同じ入力境界を同期focus bridgeで処理し、未変換時のTabやpointerによる変更をDOM callbackが戻る前に同期する。このbridgeは全surfaceがIdleである場合だけ通常routerを呼び、sourceへの二重queueを防ぐ。内部focusを処理したイベントは必要な既定focus移動を同期preventDefaultし、proxyからcanvasへ戻されることを防ぐ。Dispatch終了後にSynchronizeFocusを呼び、layoutや文書callbackは次の通常frameへ残す。Idleでない再入時はそのfocus変更入力を消費して不成立とし、対応系列の終端も消費する。後からfocus変更だけを遅延実行しない。

旧sessionの終了が非同期なら境界はEndingを継続し、新editorをactiveにしない。Pump/service.Advanceは終了に必要なTSF lock・DOM終端通知を処理し続ける。旧SessionId/Generationで取得済みの編集は旧文書の終了処理へ固定し、既存key/pointer系列の終端は旧所有表で清掃する。Ending中の新しいキー・文字・編集要求は消費し、旧文書にも新文書にも適用・再送しない。物理状態と終端までの消費表を保持するが、将来のeditor向けの文字queueは作らない。解放後に最新のfocusを再検証して新しいsession世代を開始する。通常の未変換sessionはこの境界内で解放・再接続できる契約とし、「Bをclick→入力」が同じpumpにあってもBへ入ることを確認する。

DrainChangesはUI connectionが一回だけ消費する。platformは自分のtransaction結果からTSF sink通知を作り、UI用queueを横取りしない。

### WindowsのTSF実装

Windows 10 1809以降を維持する。同じSTAでCOMを初期化し、`ITfThreadMgr.Activate`、client ID、document manager/context、`ITextStoreACP`、`ITfContextOwnerCompositionSink`を作る。既存COM初期化の所有権を確認し、成功した自分のCoInitializeExに対応するCoUninitializeだけを呼ぶ。TSF利用失敗をIMM32へ暗黙fallbackせず、機能と失敗codeをホストへ返す。

既存WindowsPlatform.PumpEventsはキーをsourceへ渡してからTranslateMessageしているため、このまま外付けserviceを呼ぶだけではTSF優先にならない。このPRでWindowsPlatformへ同threadの内部message filter登録口を追加し、取得MSG・物理状態の保存→TestKeyDown/TestKeyUp→KeyDown/KeyUp→消費/編集scope判定→公開する通常source/TranslateMessage/DispatchMessageの順へ接続する。filter tokenを解除してからserviceを解放する。内部口はfriend assemblyに限定し、OS messageをpublic eventで購読するAPIは増やさない。

TSFがeatenと返したキーはTranslateMessageとUIのキー経路へ二重に流さない。未消費の通常文字はWM_CHAR/WM_UNICHAR経路でsurrogateを組み立て、active sessionへの通常置換transactionへ渡す。Backspace、Enter、Tab、Escape等の制御文字をWM_CHARから再編集せず、編集commandと二重処理しない。TSF処理とWM_IME_COMPOSITION/IMM32の結果文字列を同時採用しない。WM_CHARとTSFによる編集をフレーム末の文字列一致で重複除去せず、取得時の経路の所有を一つに決める。

ITextStoreACPはGetText/SetText、GetSelection/SetSelection、QueryInsert/InsertTextAtSelection、GetEndACP、GetStatus、AdviseSink/UnadviseSink、view/window/幾何、必要な属性の問い合わせを実装する。plain textだけを公開し、unsupportedな埋め込みobjectやformatは仕様上のHRESULTで返す。GetTextは有効なACP範囲のUTF-16を要求bufferへコピーし、読み取り途中のsurrogateをUTF-8へ誤変換しない。書き込みの範囲はscalar境界と最大長をQueryInsertで検査する。

RequestLockはロック中の文書再入とread→write昇格を扱う。

- 直ちに許可できない同期要求はphrSession=TS_E_SYNCHRONOUS、非同期要求はTS_S_ASYNCとして保留する。
- 保留要求は一つに統合してwriteを優先し、同じSTAの安全なpump地点でOnLockGrantedを呼ぶ。
- 現在のlock中やRequestLockの制御処理からOnTextChangeを通知しない。OnLockGrantedに入った編集transaction内だけで文書を書き換え、TSF由来の変更を同じsinkへアプリ変更としてechoしない。
- アプリ側変更は別transactionで成立させ、lock解放後にOnTextChange/OnSelectionChange/OnLayoutChangeを通知する。

OnStart/Update/EndCompositionとcontextのdisplay attributesを使い、対象文節・下線・選択を共通文書へ反映する。OnEndCompositionだけからcommit/cancelを推測せず、TSFが文書へ実際に適用した最終状態を読む。OnUpdateではpRangeNewを使い、callback中にまだ更新されていない旧GetRangeだけを参照しない。アプリの明示Cancelでは終了要求後に最新の確定checkpointへ戻し、必要な文書変更通知を送る。TerminateComposition自体が元の文字列へ復元してくれるとは仮定しない。

focusとの対応はAssociateFocus/SetFocusで有効なdocument managerへ切り替え、終了時に旧associationを正確に戻す。GetWndはviewのPresentationWindow、keyboardのassociationはFocusWindowに対応させる。非活性popupにeditorがある構成はこの分離を実機検証し、ホストが未対応ならeditorを含むPopupの呼び出し元がHostedOnlyを選び、Native必須の場合は編集未対応を返す。表示済みのWindowを暗黙にHostedへ移送しない。OS focusを奪うActivateで補完しない。

COM境界はMicrosoft.Windows.CsWin32 0.3.367を使用する。NativeMethods.txt/jsonからCOM interface・vtable・構造体・CLSID・P/Invokeを生成し、手書きのABI宣言を保持しない。allowMarshaling=falseと全メソッドのPreserveSigを指定し、型付きnative pointerと所有handleで参照数を管理する。callbackはCsWin32が生成するInterfaceとvtableを.NET ComWrappersへ接続し、IUnknownだけをruntimeの実装へ委譲する。HRESULT、BOOL、文字数とbuffer長はSDK生成型を通して受け渡す。実装型・sinkは強参照とcookieで保持し、.NET例外をCOM境界から出さない。reflectionによる動的RCW生成を前提にせず、Windows x64/arm64のNative AOTで検証する。既存managed Windows backendへ接続し、新しいC++/C ABIは導入しない。

### geometryと候補UI

layout成功後のcaret、範囲矩形、viewportをsurface座標からHosted原点・TextBoxの表示offset・祖先clipを合成してPresentationWindowのclient論理座標へ変換する。Windows adapterが当該WindowのDPIとClientToScreenを一度だけ適用し、TSFへscreen pixelを返す。FocusWindowのDPIを代用しない。

geometryには文書範囲ごとの矩形、UTF-16位置とaffinityを持つcaret stop、DocumentRevision/LayoutRevisionを付ける。TSFの任意範囲問い合わせを処理できるよう、active caret一つだけを保存しない。要求中の文書revisionと一致するlayoutがなければGetTextExtはTS_E_NOLAYOUTとし、次のlayout後にOnLayoutChangeを通知する。再入中にWidgetをlayoutしたり、古い文字位置へ適当なrectを返したりしない。clipされた範囲はpfClippedへ反映し、最小化・非表示はTSFの規定どおり扱う。GetACPFromPointも同じ対応表と座標変換を使う。

候補表示はSystemを既定とする。[UI-0015](UI-0015-custom-ime-candidate-presentation.md)の任意ITextInputCandidateSessionとImeCandidateView/controllerで、対応するTSF候補だけを独自描画できる。候補protocolのcapabilityは通常のComposition/Geometryから独立させ、Browser textareaではfalseとする。候補windowはUI-0014のImeCandidates/Candidateで扱い、Menuへ変換しない。System候補・独自候補の出現やpointer操作を、編集元の外側pressやfocus喪失としてchain終了させない。読みウインドウ等の対象外UIはOS/TIPへ任せる。

### Browserの入力とフォールバック

標準経路はfocus可能なtextarea proxyとする。display:none、disabled、画面外の固定座標を使わず、active editorのcaret付近へCSSの論理座標で配置し、視覚的に二重描画しない。canvasのbounding client rect、CSS寸法、VisualViewportを考慮し、canvasのphysical pixelをCSS pixelと混同しない。proxyは必要なfullscreen要素の内部へ置く。OS候補・ソフトkeyboardの最終位置をアプリが保証できるとはしない。

compositionstart/update/end、beforeinput、input、selectionchange、keydown/upを一つのJS adapterで取得し、session/generationと単調な編集sequenceを付ける。beforeinputがcancel不可または来ない場合もinputで実際のvalue/selectionを読み、前snapshotから一つの置換へ正規化する。compositionend.dataを追加してからinput.valueも追加する二重commitを行わない。DOM valueを入力正本として文書へ一度反映し、アプリ側変更だけをguard付きでDOMへ戻す。

JSの取得と抑止はDOM callback内で同期的に行い、requestAnimationFrame上でmanaged文書とElementへ反映する。isComposingだけで終端キーを判定せず、composition境界と同じイベント列に属するkeydown/upも追跡し、必要なlegacy IMEキーの観測を合わせる。DOMが使用したEnter/Escape/矢印を後でMenu/Buttonへ再配送しない。clipboardの貼り付けも実際のDOM編集を一経路で採用する。

canvasとproxyは同じBrowser Windowの論理focus領域とする。このPRでBrowserPlatform/Browserのwindowing JSへ内部のproxy登録口を追加し、BrowserTextInputServiceがWindowId・session世代付きの登録tokenを所有する。focus前に登録し、終了・proxy削除時に解除する。WindowState.IsActiveの判定はdocumentがactiveかつactiveElementがcanvasまたは当該Windowの登録proxyであることへ変更し、managerのkeyboard targetを保つ。WindowSystem adapterを使う場合はWindowSystem.KeyboardTargetも維持する。canvasのblur入力だけをUI側で抑止してIsActive=falseを残す実装にはしない。

Browser側のfocus観測はrelatedTarget、activeElementと登録世代で内部移動を判定し、canvas↔proxyの転送途中に一過性の非活性通知やFocusData(false)を発行しない。実際のアプリ外blur・ページ非表示・proxy削除ではWindow状態と入力のfocus喪失を通常経路へ一度通知し、sessionを終了する。proxyへのフォーカスによる初期pointerの取りこぼしと、モバイルでのuser activation/keyboard表示失敗も実機確認する。

EditContextは将来の任意backendとし、初期必須機能にしない。textareaが利用できない環境ではCommittedText/Composition capabilityをfalseにし、TextBoxはReadOnly相当として未対応理由を返す。キーコードから日本語を作るfallbackは設けない。

### focus移動・終了・寿命

既定はfocus移動、Menuを開く、owner非活性化、非表示化、取り外し、window終了でcompositionをCancelする。明示commitしてから移動したいアプリは次の手順を使う。

1. connection.SuspendAsync(TextCompositionEnd.Commit)を待つ。connectionが保持するsessionを外部から取得する必要はない。
2. owner threadのIdleでfocus/open要求を行う。
3. 返されたtokenを、focus移動の成立またはMenu open guardへの引継ぎまで保持する。

省略形のSuspendAsyncはCancelを選び、いずれも内部ではRequestEndを使う。待機の取消でも終了処理とtoken清掃を続け、既に確定した文字をrollbackしない。キャンセル中は新editorをactiveにせず、終端キーと遅れた編集を旧sessionへ固定する。composition中の文書へアプリのTryReplaceを競合させない。

接続の状態はInactive→Active→Ending→Released。Endingはbackendのcomposition終了と文書commit/restore、未完了key系列の清掃、callback queueの確定まで継続する。TSF/DOM通知が終了後に届いても、新sessionへ使い回さない。古い文書を復元する必要がある通知は清掃経路で処理し、generationの違う編集要求は破棄する。

frame順はPlatform Pump→TextInput service.Advance→TextInputKey/通常Input収集・Popup.Dispatch→TextInput connection.Advance→Popup.Advance→Automation/Diagnostics→UI layout→geometry公開→Record/提示とする。Pumpは上記入力境界で分割でき、そのたびに全surface IdleでSynchronizeFocusを行う。frame内のcallbackやAutomationでfocusが変わった場合も次のOS入力取得前に同じ境界を通す。connectionのSuspendAsyncをownerのRegisterMenuGuardへ渡す。Popup.Advanceはcomposition終了のdrainとguard token取得を待ってからMenuをOpenにし、tokenをMenu終了まで保持する。callback中に終了を同期waitせず、開く途中の失敗・取消でもtokenを返す。PopupのBindLifetimeにはconnection停止とIsStopped待ちを含むtokenを登録する。

終了は新規編集停止→RequestEnd(Cancel)→TSF lock/DOM通知/Job/キー清掃→UIからの接続解除→sink unadvise・focus association復帰・context pop→document managerとthread managerをrelease/deactivate→proxy/message filter解除→Window解放→Platform解放とする。Window/COM store/documentを、未完了のlock要求や編集sessionから先に解放しない。停止待ちの間もPump/Advanceを続ける。

### 対応環境

| 環境 | 初期backend | 変換・候補 | 未対応時 |
| --- | --- | --- | --- |
| Windows 10 1809以降 | TSF + ITextStoreACP、同STA | 文書内preedit、既定System候補。任意の独自描画はUI-0015 | TSF失敗は明示失敗。独自候補未対応はSystem |
| Browser | 同canvasのtextarea proxy | DOM composition、Browser/OS候補。独自候補capabilityはfalse | textarea未対応は編集不可、独自候補のみ未対応ならSystemを継続 |
| Headless | 共通文書とfixture service | 模擬preedit/commit/cancel | OSのIME検証を意味しない |
| Linux等 | 今回はnative adapterなし | 未対応 | ReadOnly相当。IBus/Fcitxは別設計 |

### AutomationとDiagnostics

UiRoleにTextBox、UiAutomationActionsにEditを追加し、実際のTextBoxだけを編集対象とする。任意のUI.TextEditing.Automationが通常のfocus/編集command経路を使い、FillAsyncと確定値のassertionを提供する。OnTextChangedの直接実行やDocumentプロパティの差し替えで操作を代用しない。

```diff
+namespace Lumyte.UI.Automation
+{
+    // UiRole.TextBox=3、UiAutomationActions.Edit=4を追加し、既存値を変更しない。
+    public interface IUiAutomationTextHost
+    {
+        // hostに結び付いたsurfaceのcontrol groupを対象とし、内部でowner threadへ配送する。
+        // composition/Ending/取得済み編集のdrainと物理中立を待ってから排他を返す。
+        // 待機中はleaseを保持せず、物理IMEの入力・終了を継続できる。
+        ValueTask<IUiAutomationInputLease> AcquireTextInputAsync(
+            CancellationToken cancellationToken = default);
+        // UI threadのIdle。実入力scope・編集可否・compositionなしを再検査する。
+        // input lease下で通常の「全選択→置換」commandを一度だけ実行する。
+        void Fill(UiSurface surface, UiAutomationId editor, string text);
+        // committed値。inspection未許可はNotSupportedException。
+        string ReadValue(UiSurface surface, UiAutomationId editor);
+    }
+    public sealed class UiLocator
+    {
+        public Task FillAsync(string text, UiWaitOptions? options = null,
+            CancellationToken cancellationToken = default);
+    }
+    public sealed class UiExpect
+    {
+        public Task ToHaveValueAsync(string text, UiWaitOptions? options = null,
+            CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation
+{
+    public sealed record UiAutomationTargetOptions
+    {
+        public bool AllowTextInput { get; init; } = false;
+        public bool AllowTextInspection { get; init; } = false;
+    }
+}
```

Client/Contractsの次の抜粋は、UI-0010/0011の提案宣言に対して追加・変更する。既存planの文字列上限32768は維持し、JSON escape等で収まらないtextは送信前に拒否する。文字数上限とは別にencoded requestの上限も適用する。

```diff
+namespace Lumyte.Diagnostics.UI.Automation.Client
+{
+    public sealed class UiRemoteLocator
+    {
+        public Task FillAsync(string text, CancellationToken cancellationToken = default);
+    }
+    public sealed class UiRemoteExpect
+    {
+        public Task ToHaveValueAsync(string text, CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation.Contracts
+{
-    public sealed record UiRemotePlan(int SchemaVersion, string Kind,
-        UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null,
-        UiRemotePoint? Position = null, string? Predicate = null, int? Count = null);
+    public sealed record UiRemotePlan(int SchemaVersion, string Kind,
+        UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null,
+        UiRemotePoint? Position = null, string? Predicate = null, int? Count = null, string? Text = null);
-    public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots,
-        Guid? ParentTargetId = null, Guid? PopupId = null, Guid ControlGroupId = default);
+    public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots,
+        Guid? ParentTargetId = null, Guid? PopupId = null, Guid ControlGroupId = default, string AutomationCode = "",
+        bool AllowTextInput = false, bool AllowTextInspection = false);
+}
```

Fillは最初のTab/focus変更より前にAcquireTextInputAsyncを呼ぶ。hostはcontrol group全体の物理composition・Ending・未反映DOM編集・未完了TSF lockと物理押下の解消を同じ操作期限まで待つ。待機中はgroup入力leaseを取らず、focusを変更せず、IMEの確定/取消操作とPump/Advanceを通常どおり進める。TextDocument.Compositionがnullという条件だけで取得しない。

取得と操作は次の順で行う。

1. owner threadのIdleで通常group leaseの予約と上記条件の再確認を一つの処理にする。間にcompositionが始まっていれば排他を返して待機へ戻り、進行中の物理compositionを自動cancelして取得しない。
2. 取得後はhostの入力gateで新しい物理編集の割り込みを防ぎ、対象の一意性・可視性・有効性・編集可能性・menu scopeを再検査する。
3. 同じlease内でTab等によるfocus手順を行い、最後に対象ID・revision・scopeを再検査してFillする。

focus手順のlease借用と最外側の操作による解放は[UI-0009](UI-0009-ui-automation.md#キー操作と後処理)に従う。公開FocusAsyncを呼んで二重取得せず、送信済み入力の清掃まで同じleaseで保護する。

Fillは通常Pasteと同じ「全選択→置換」commandを一回使い、一度送信を開始したら自動再送しない。期限・取消時は未取得なら入力なし、取得済みなら通常のlease清掃を待って結果を確定する。OS IMEが日本語を生成したという試験結果にはしない。

Diagnosticsのhost wrapperは[UI-0010](UI-0010-diagnostics-ui-automation.md#ジョブ状態期限結果)のcapability転送規則をAcquireTextInputAsyncにも適用し、このlease取得成功でもInputMayHaveBeenSent=trueを記録する。

DiagnosticsはOverrideInputかつAllowInput/AllowTextInputでfill、ObserveかつAllowTextInspectionで値のassertionを許可する。Popupの子は親の許可とのANDとし、UI-0011のcontrol group・Sequence・未知結果の規則を維持する。snapshotの既定内容へ本文/preeditを追加せず、ログとtraceも入力内容を既定では記録しない。画像取得は従来のScreenshot許可に従う。

UI用plan schema version 2へkind=fill/expect-valueとnullableなtext field（最大32768 UTF-16 code unit）を追加する。fill/expect-value以外ではtextを拒否し、textは非nullとする。roleの文字列にtextboxを追加する。RuntimeのUI subsystem schemaを2へ上げ、catalogで対応を公開する。新clientはcatalogを確認し、未対応serverでは送信前にUnsupportedとなる。Runtimeは既存version 1の閉じたrequestも継続受付し、version 1では新kind/fieldを拒否する。Diagnostics protocol 2と既存Operation IDは変更しない。

ClientのUiRemoteLocator.FillAsyncとUiRemoteExpect.ToHaveValueAsyncはversion 2 planを生成し、ui.automationのaction-start/observe-startへ渡す。textを含むplanもTargetIdを含め同じsequence payloadとして比較し、重複要求を再実行しない。Contractsの追加フィールドは上記API diffに従い、新しい汎用スクリプト実行口は作らない。

候補本文のinspection、候補専用lease、選択・確定等の型付き操作とschema version 3は[UI-0015](UI-0015-custom-ime-candidate-presentation.md)で追加する。Fillのcomposition終了待ちとは分け、候補を一般Click/Fillへ公開しない。

## 検討した代替案

- WindowsをWM_CHARだけで実装するとcomposition、再変換、周辺文書、候補位置の契約が不足する。TSFを正式経路にする。
- TSF用shadow文書をframeごとにUIへコピーすると、同期GetText/SetSelectionの間にrevisionがずれる。同threadの共通文書を使い、Elementへの通知だけを遅延する。
- Browserのcompositionend.dataをそのままappendするとinputによる確定と重複する。DOMの実value/selectionを一回正規化する。
- 候補Windowを通常Menuへ置換するとfocusとIMEの操作protocolが衝突する。Systemを既定とし、独自描画はUI-0015の専用境界へ分ける。
- IME消費後にUI側だけでキーを無視すると、既存InputSystemのショートカットや対になるUpが残る。取得時の消費と系列清掃を接続する。

## 結果と影響

Composition、Windows TSF、Browser、headless、自動操作で同じ編集文書と確定callbackを共有できる。Native/Hostedの座標とfocusを分離し、候補位置とMenuの入力競合を明示できる。

一方、TSFのCOM lock規則、Platformのmessage filter、Browserのfocus proxy、Parleyのcaret対応が実装範囲に加わる。既存のWidget・キー設計だけでIME対応済みとは扱わない。一般のText表示やLinux IME対応はこの判断だけでは完成しない。

## 検証方針

- 文書のpreedit置換、選択、部分確定後のcancel/再変換、同revision競合、改行正規化、上限、surrogate、結合文字・emoji ZWJとcallback集約を確認する。
- TSFのlock再入、read→async write昇格、同期拒否、sink通知のecho防止、部分buffer読み、QueryInsert、属性、COM失敗・終了中遅延要求をfixtureで確認する。
- 文字位置について、UTF-8/UTF-16、合字・bidi、Hostedの原点とclip、水平表示offset、非一様DPI、別monitor、未layoutのTS_E_NOLAYOUTと通知を確認する。
- IME入力のEnter/Escape/矢印/Tab消費、UIへDown送信後のUp消費、Menuを開く途中のcancel、focus復帰、世代の古いcommitを確認する。
- 同一pumpの「入力→左移動→入力」「Tab→入力」「A編集中→Bをclick→入力」で適用先と順序を検証する。Ending中の新規入力は消費、旧世代の終端は清掃、新世代へ再送しないこと、bridge再入時にfocus変更を遅延成立させないことを確認する。
- Browserのbeforeinput欠落/非cancelable、compositionend/inputの順序差、貼り付け、canvas↔proxy focus、viewport resize、fullscreen、モバイルkeyboardを確認する。内部focus転送でWindowState.IsActive・managerのkeyboard targetを維持し、外部blur/ページ非表示/削除では一度だけ終了することを確認する。
- Windows実機でMicrosoft日本語IMEの入力/文節変換/取消/再変換、OS候補選択、focus移動、Native/Hosted popupを確認する。fixture結果と実機結果を分け、Windows x64/arm64とNative AOTを既存runnerで検証する。
- Automation/DiagnosticsでFill→確定値assertion、物理composition中の待機、親子lease、古いschema/権限/上限、取消後の再送防止を確認する。実OS IMEの候補操作をFillの成功で代用しない。
- Fillの待機では、別TextBoxの変換中に最初のTabを送らず、手動確定を妨げず、取得直前のcomposition開始でも再待機することを確認する。取得後のfocusと編集が同じlease内であること、wrapperのInputMayHaveBeenSentも確認する。
- Button内TextBoxの位置では実入力とProbeが親Buttonを操作せず、Clickが偽成功しないことを確認する。
- SuspendAsync(Commit)で確定callback・session解放を待ってfocus/Menuへ移ること、token保持中の再開抑止、取消時のdrainと一度だけの解放を確認する。
- 注入したmanager/resolverでKeyboardRoot・PresentationRoot・InputWindowIdを明示対応し、異なるSourceIdや古い世代を拒否することを確認する。独自候補のcapability、System fallbackと専用操作はUI-0015の受け入れ条件で検証する。

## 参考資料

- [TSF thread manager](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nn-msctf-itfthreadmgr)
- [ITextStoreACPとdocument lock](https://learn.microsoft.com/en-us/windows/win32/api/textstor/nf-textstor-itextstoreacp-requestlock)
- [composition sink](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nn-msctf-itfcontextownercompositionsink)
- [TSF key処理](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfkeystrokemgr-keydown)
- [GetTextExtの座標とlayout](https://learn.microsoft.com/en-us/windows/win32/api/textstor/nf-textstor-itextstoreacp-gettextext)
- [Browser beforeinputの制約](https://developer.mozilla.org/en-US/docs/Web/API/Element/beforeinput_event)
- [Parleyのcursor/selection](https://docs.rs/parley/0.12.0/parley/editing/index.html)
- [既存Windows backend](../../../src/Platform/Lumyte.Platform.Windows/README.md)、[Browser backend](../../../src/Platform/Lumyte.Platform.Browser/README.md)
- [Text描画](../graphics/GRAPHICS-0024-text-rendering.md)、[Popup](UI-0011-popup-windowing-and-fallback.md)、[Diagnostics](UI-0010-diagnostics-ui-automation.md)

### イベント配送とIME文書の境界

UiKeyInput・UiFocusChangedInput・UiKeyCancelInputはUI-0006の共通hookへ配送し、TextBoxのInputHandlerが編集を判断する。IMEのpreedit/commit等は引き続きsession・文書revision付きのTextDocument経路で適用する。物理キーから文字列を合成せず、同じ確定文字をUiInputと文書transactionへ二重適用しない。文書変更のOnTextChanged/OnCompositionChangedは既存のframe反映で呼ぶ。

検証にはTextBox相当WidgetがButton内でDown・ドラッグ・キーを消費して外側Buttonを発火させないこと、focus/key captureの途中取消とIME消費済みキーの非再送、InputHandlerなしの編集不可targetの遮断を含める。TextBoxとOS接続の実装はこのPRの範囲であり、先行の汎用イベント配送だけで実装済みとはしない。

### PR 11で具体化した接続API

実装packageは`Lumyte.TextEditing`、`Lumyte.Platform.TextInput.Abstractions`、`Lumyte.Platform.Windows.TextInput`、`Lumyte.Platform.Browser.TextInput`、`Lumyte.UI.TextEditing`、`Lumyte.UI.TextEditing.Parley`、`Lumyte.UI.Windowing.TextInput`、`Lumyte.UI.TextEditing.Automation`に分ける。AutomationとDiagnostics ContractsはBCLの文字列・DTOだけを共有し、TextBoxやnative backendへの依存を追加しない。

- `TextEditingCommands.ReplaceAll(TextDocument, string, TextEditOrigin)`は全選択置換を一transactionで行い、composition中はfalseを返す。Fillもこのcommandを使う。
- `UiSurface.FindElement(UiAutomationId)`は同じsurfaceの現在のattachmentを借用取得し、detach後のIDにはnullを返す。`HitTest(Vector2)`も汎用機能として実装し、focusはUI-0006の`TryFocus(UiElement?)`を使う。UI coreにTextBox分岐は追加しない。
- `UiTextAutomationHost(IUiAutomationHost, UiTextInputConnection, UiImeCandidateController? = null, bool allowTextInspection = false)`が既存hostを包む。routing・screenshotは元hostへ転送し、未対応機能はNotSupportedExceptionを返す。本文をReadValueへ渡すのは明示inspectionだけで、snapshotやtraceへ転記しない。
- `ITextInputInputGate.AcquireInputGate(bool allowComposition = false)`は同じownerで解放するIDisposableを返す。通常Fillと候補操作はそれぞれのidle条件を再検査してからgateを取得する。`IsCandidateInputIdle`は既存compositionを許すが、取得待ちの物理押下・backend処理は許さない。
- `UiTextInputConnection.GetFocusedEditor(UiAutomationId)`、`AcquireInputGate(bool)`、`IsInputIdle`、`IsCandidateInputIdle`は借用host adapter向けの境界とする。OS backendへはWidgetでなく`UiTextDocumentNavigator`を渡す。
- `BrowserTextInputService.RegisterFocusBridge(Action dispatchInput, Action synchronizeFocus, Func<bool> canDispatch)`はDOM callback内でPlatformの取得、文書反映、hostの入力配送とfocus同期を行う。hostは通常InputRecordとTextInputKeyを共通Capture.Sequence順に配送し、境界ごとにSynchronizeFocusする。bridgeが利用できない境界の入力を後から再生しない。

`ParleyTextOptions2D.IncludeEditingGeometry`は既定falseとし、UI providerだけがtrueにする。一般の文字描画にcaret列挙の費用を加えない。Parleyはnative ABIを2へ上げ、`lp_layout_editing_view`でvisual caret、前後のvisual位置、選択矩形を返す。メモリはlayout handleが所有し、managed側はhandle解放前にコピーする。UTF-8 offsetをUTF-16へ対応付け、grapheme境界で絞る。旧native binaryとの混在はengine作成時に拒否する。

WindowsのCOM宣言・callback vtableはCsWin32がWin32 metadataから生成し、SDKのnative vtable順を保持する。contextのITfTextEditSinkからread cookie内でGUID_PROP_ATTRIBUTEを列挙し、GUID atom→display attributeを解決してBCLのspanへコピーする。COM pointerとcookieをUIへ持ち出さない。managed fixtureのlock試験と、Windows/TIP実機のcomposition・候補試験を区別する。

実機用の接続例は`samples/Lumyte.UI.TextEditing.Sample`に置く。使用fontをホストが登録し、System候補を既定、PreferCustomを明示optionとして提供する。BrowserのDOM試験はsynthetic compositionを使い、OS IMEの生成・候補動作を実証したとは扱わない。

## 検証記録

2026-10-29、Linux x64、.NET SDK 10.0.401で警告をエラーとしてReleaseを検証した。TextEditing 21件、managed TSF store 6件、Diagnostics 42件、Window連携31件、Automation 49件、UI本体178件、Parley 114件、Input 21件、Input Advanced 110件の計572件が成功した。新規TextEditingとTSF storeはDebugでも確認した。TSF fixtureはOSのCOMを呼ばない。

native ParleyはRust 1.89（依存の最小要件1.88以上）でbuildし、Rustの17テストが成功した。caret列挙を要求しない通常layoutと、bidi/ligatureを含む編集geometryを区別して確認した。リポジトリ既定のRust toolchainを変更したものではない。

実Chromiumではtextareaと既存Window moduleを接続し、composition/inputによる二重確定防止、session/sequence、同期A→B focus、旧Tab終端、物理編集gate、外部blurとproxy清掃の18項目を確認した。既存Browser Window JSの8テストも成功した。これはsynthetic DOM eventによる試験で、.NET Browser hostの実運用やOSのIME候補操作の証明ではない。

新規8packageのpackと、Parleyを参照しないNuGet-only consumerで文書・生成TextBox関数・snapshot・wire schemaの実行を確認した。Browser JSの出力copyも確認した。既存Parleyの正式native packageはlinux-x64/linux-arm64/win-x64/win-arm64のCI成果物を揃えるguardを維持し、この環境では4種類が揃わないため正式packを完了していない。UI.TextEditing.Parleyのmanaged packと、ローカルnative binaryによる実際の文字組み試験とは区別する。

WindowsのsampleはReleaseでbuildした。Windows実機のTSF/日本語TIP、x64/arm64でのOS操作とWindows Native AOT、Browserの実IME/mobile keyboardは未検証として残す。検証済みのfixtureをこれらの成功として扱わない。

### Surface単位の自動入力接続の検証

TextBoxの構築引数に入力システムを含めず、同一Surface内の複数editor間でsessionが正しく切り替わること、複数Window/Surfaceが別のhandlerを解決すること、登録解除・二重登録・切断時の寿命を確認する。CsWin32生成vtable経由のmanaged callback往復とIUnknownの同一性はTSF/OS APIを呼ばないテストで確認し、実Windows/TIPでの変換・候補表示・focus復帰の検証とは区別する。

2026-10-30のCsWin32移行ではCOM fixture 8件とTextBox/接続 25件をReleaseで確認した。Linux x64のNative AOTバイナリでもCCWのGetStatus・QueryInterface・IUnknown同一性を確認した。この確認ではTSF/OS APIを呼ばず、Windows上のSTA・日本語TIP・実候補Windowの検証を代替しない。

### Controlled valueと再構築時の内部状態

文字列と変更イベントを利用者向けAPIとする。TextDocumentはUIから独立したBCLのまま、TextBoxの論理状態としてUIが作成・保持する。通常の利用者に文書・runtime・build scopeを要求しない。現在の低水準hostではBuildを再実行してUiSurface.Rebuild(Build())へ新しい宣言を渡し、次のUpdateLayout境界で反映する。WidgetChildrenを二回目に初期化しない。Signalによる構築関数の自動再実行はPR 15の対象である。

同じ親Elementと登録子slotの範囲で、型とKey（nullなら位置と型）が一致するTextBoxElementを保持し、文書、選択、水平offset、focusを維持する。Keyは任意のstringで、同じ兄弟slotの重複はcommit前に拒否する。型やkeyの変更・除去では新しい状態となり、旧IME sessionは既存のdrain契約で終了する。同じ論理elementのAutomationIdは宣言置換後も維持するが、旧Widget宣言は実体から参照されなくなる。

確定値が同じTextの再提示では文書を書き換えず、未確定文字、文節選択、caret、IME候補sessionを保持する。異なる外部値では選択を有効なscalar境界へclampしてApplication更新を一回行う。compositionまたはbackendの遅延処理がある間は終了・取消と解放を待ち、古いnative lockへ新しいモデル値を混在させない。待機中は新しい外部値を最新の宣言から採用し、値の適用をTextChangedへ再通知しない。入力を拒否・変換するアプリも、採用する値で再構築する。新しい宣言がなければ任意のモデル変数の更新を推測しない。

UI coreは#77からのElement基盤を使い、TextBoxの型を判定しない。TextBoxElementが文書・選択・水平offset・preeditを継続保持し、同じidentityの設定更新でコピーしない。入力不適格化・除去時だけcaptureを取り消し、継続するキーrepeatを中断しない。focus bookmarkと編集connectionもElement identityを保持する。OS bridgeへWidgetを保持させず、CPU layout providerと文書を使う。

準備失敗では現在のElement・文書・購読を維持する。公開後のlayout失敗は更新を中断し、先行変更をrollbackしない。独自componentの状態は型付きElementに置き、OnReconcileで新しいWidgetへ転送しない。

UI-0002からの共通再構築基盤へ接続した構成で、全体Release buildを警告・エラー0件で確認した。全35スイートは1,926件成功・75件skip・失敗0件。UI core 197件とTextBox/接続32件には、再構築時の選択・preedit・session・キーrepeatの保持、key付き並べ替え、外部値の遅延反映、入力拒否・加工時のecho抑制、準備失敗と終了中のcleanupを含む。Windows実機IMEの検証範囲は上記のままである。

## 全体レビューに基づく更新境界

IME geometryの公開keyはsession ID、layout参照、文書revision、絶対原点、clip、scaleとする。同じkeyならrange/caret配列の生成とbackend.UpdateGeometryを省略する。keyは公開成功後だけ確定し、新sessionや移動・scroll・clipの変更を取りこぼさない。Parley rangeは開始offset順とprefix最大終端offsetで索引化し、交差しない区間を二分探索で飛ばす。Bidi、結合文字、ligatureを含むnative selection矩形との等価性を検証する。

### Element分離後の再検証

ReleaseでTextEditing 38件、UI本体214件、Automation 50件、Windowing 36件が成功した。
TextBoxElementは文書・選択・preedit・offsetを維持し、同じWidget宣言を複数Surfaceへmountしても
文書を共有しない。connectionはElementを保持し、宣言交換時の文書・editor参照の付け替えを行わない。
providerとcallbackは採用済みWidgetから読み、入力handlerはElement.Surfaceの登録から解決する。
Windows TSFのCsWin32生成COM型を使うテスト8件と、実Chromiumでのtextarea lifecycle
18項目も成功した。後者は合成DOMイベントの確認であり、OS IMEの手動操作結果とは区別する。

TextBoxElementはhostのサービス検証indexへ登録する。IsReadOnly/LayoutProviderの変更を
HostServicesChangedで通知し、文書や色だけの更新とは分離する。購読や文書状態の寿命は従来どおりElementが持つ。
