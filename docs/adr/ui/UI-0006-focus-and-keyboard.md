# ADR-UI-0006: surface内フォーカスとコンポーネントへのキーボード配送

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 5、前提: PR 4

## 背景

[UI-0005](UI-0005-pointer-dispatch-and-capture.md)のButtonへキーボード操作を追加する。pointerとキーで同時にClickしたり、focus移動後のUpで以前のButtonが発火したりしないよう、同じ押下ownerに統合する。

## 決定

### 公開API

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。KeyはInput.Abstractionsの既存型で、UiInputとDispatchはPR 4で導入したものを拡張する。

```diff
+namespace Lumyte.UI
+{
+    [Flags]
+    public enum UiModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Meta = 8 }
+    // record構築時には検査しない。modifier未知bit・repeat UpはDispatchで拒否。
+    public sealed record UiKeyInput(Key Key, bool IsDown, bool IsRepeat = false,
+        UiModifiers Modifiers = UiModifiers.None) : UiInput;
+    public sealed record UiFocusChangedInput(bool IsFocused) : UiInput;
+    public sealed record UiKeyCancelInput(Key Key) : UiInput;
+    public readonly struct UiInputContext
+    {
+        // 同surfaceの適格なReceiverへfocusを要求。切断/破棄済みもfalse、復活させない。
+        public bool RequestFocus();
+        // 現在のfocus対象か。通知中も最新の状態を返す。
+        public bool IsFocused { get; }
+        // key ownerへの配送か。先に解除したUp/Cancelでもtrue。
+        public bool IsKeyCaptured { get; }
+        // key Down専用。receiver単位で系列を取得し、他ownerならfalse。
+        public bool CaptureKey();
+        // 自分のkey ownerだけを解除し、UiKeyCancelInputを通知する。
+        public bool ReleaseKeyCapture();
+    }
+    public sealed class UiSurface
+    {
+        // 初期null。OSのWindow focusとは独立したsurface内の対象。
+        public Compose.Definitions.Widget? FocusedWidget { get; }
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 既定false。Buttonだけがコンストラクターでtrueに設定。
+                [ComposeParameter] public bool Focusable { get; set; }
+            }
+        }
+    }
+}
```

キーはUI-0005のOnInputへ配送し、focusの取得・喪失も型付きUiInputとして通知する。Windowを跨ぐfocus管理はこのPRでは公開しない。[UI-0011](UI-0011-popup-windowing-and-fallback.md)でPopupのfocus bookmark・復帰・候補移動を追加する。Focusable=trueのカスタムWidgetもキーをOnInputで処理し、必要ならキー系列を取得できる。Enter/SpaceのClick認識はButton自身のoverrideへ置く。

### focus候補と移動

各コンポーネントのGetInputChildrenが定める可視・有効・HitTestVisibleな入力対象ツリーを前順で走査し、Focusable=trueの要素を候補にする。Buttonは背景を含めず、独自コンポーネントはプロパティ名によらず入力対象の子と巡回順を定義する。clip外や0寸法の要素もTab候補には残し、スクリーン内にあることをfocusの条件にはしない。明示的な非表示・無効化等で操作不能な要素は除外する。

非repeatのTab Downで次候補へ、Shift付きなら前候補へ移る。nullからは先頭/末尾、端からは循環する。候補なしならfalse、一候補でも巡回キーはtrueとする。Control/Alt/Meta付きTabは予約せずfalseとする。

Tab Downを処理したらキーのUpまで消費状態を保持し、repeat Downと対応Upもtrueとする。候補が途中で消えてもゲームへUpだけを流さない。FocusLostではこの消費状態を含めて全入力状態を解除する。

pointer Downを処理する各Widgetはcontext.RequestFocusで自分へのfocusを要求する。ButtonとTextBoxも同じ経路を使い、surfaceは具体型を判定しない。Focusable=falseや非適格な対象では要求はfalseで変更しない。focus要求のない背景上のDownでは既存focusを解除する。ポインター押下が別ownerに拒否された場合はfocusも変えない。ホストはsurface間で切り替えるとき旧surfaceへFocusLostを渡す。

### 確定した入力候補と宣言の扱い

UI本体からKeyを使うためLumyte.Input.Abstractionsだけを追加参照し、InputSystem・取得source・OS backendには依存しない。Focusableは通常の宣言プロパティで、setterは無効化しない。UpdateLayout内の入力情報更新で有効性・入力対象の子・Focusableを反映し、focus候補を再構成する。layoutは不要でも専用のInvalidateInputがあれば同じ経路で更新する。Dispatch・FocusedWidget getter・context.RequestFocusはlayoutを起動せず、確定済み候補だけを使う。

候補は各Widgetの入力情報と同じ世代で管理し、clip外・0寸法の候補を除外しない。所有するBackground等ではなくGetInputChildrenで公開した対象だけをたどる。候補の比較と現focusの検索は参照同一性で行う。rootの初回UpdateLayoutを成功させてから通常入力を配送する。

### キー押下とpointerとの競合

Buttonのownerを`None / Pointer(id) / Key(key)`へ拡張する。focused Buttonが空いているとき、modifierなしのEnter/Space非repeat DownでKey ownerを取得する。IsPressedをtrueにし、DownではClickしない。repeat Downで認識を再開始しない。

同じキーのUpでownerと消費状態を解除し、Buttonが同じsurfaceへ所属していてfocusも維持されているならClickの認識を完了する。OnClickの取得時点・置換・解除・未設定時の動作と、callback中の変更・再入・例外は[UI-0005のcallback契約](UI-0005-pointer-dispatch-and-capture.md)に従う。

Key owner中のpointer Downや別のactivation keyは認識を追加しない。Pointer owner中のactivation keyも押下を追加しない。キーごとの消費状態を保持し、認識を開始しなかった競合キーも対応Upまで一貫して消費する。

focus移動、Focusable=false、取り外し、非表示化、無効化、HitTestVisible=falseではキーownerをClickなしで解除するが、既に消費したキーは物理Upまで保持する。FocusLostとsurface破棄だけは消費集合も空にする。focusを失った元Buttonへ同じキーのUpを配送しても再発火しない。

pointerのcaptureはTab移動だけでは解除しない。従ってpointer ownerのButtonはcapture規則に従い、focused Buttonは別要素になり得る。キーClickとpointer ClickのownerはButtonごとに排他とする。修飾キーの状態取得とOS focusの観測は利用側に残す。

### キーcaptureと消費状態

surfaceはkey→Widgetのcapture表と、対応する物理Upまでの消費集合を分ける。CaptureKeyはkey Downからだけ呼べ、他Widgetのownerや既に消費中の系列ならfalse。既存ownerへのrepeatは再取得してもtrueだが、ownerなしのrepeatで新しいcaptureを開始しない。明示ReleaseKeyCaptureは当該receiverのownerだけを解除しUiKeyCancelInputを通知する。

captureしたコントロールはrepeatとUpを直接受ける。Handledで消費するだけでcaptureしないDownは、repeatとUpを消費集合で止め、再配送しない。一般の編集コントロールがキーrepeatや終端を必要とするときも、同じCaptureKeyを使う。focus移動・明示取消・入力対象からの除外ではownerだけを外し、消費集合はUpまで残す。UiKeyCancelInputの再送は消費中ならtrueであり、認識を繰り返し取消すことはない。

FocusLostとDisposeはpointer/keyのowner・消費集合・focusを、通知前にまとめて解除する。通知が失敗しても残りの取消とfocus喪失を試み、一つの失敗は元例外、複数はAggregateExceptionとして返す。Disposeでは最後に全Widgetを切断する。Down handlerの失敗でもそのownerを取り消し、終端まで消費する。handler中のFocusLostが解除した消費状態を、古いDown処理の復帰で復活させない。

focus変更も先に状態を変更して旧キーownerへ取消を通知し、旧focusの喪失、新focusの取得を通知する。通知から同期再入した場合は新しい変更を優先し、古い取得通知を省略する。phase・thread・再入の拒否は追加しない。Upのcallbackで始めた新しい系列は、元のUp callbackの例外で取り消さない。

### 入力検証とIME接続

UiKeyInputはUI-0005の汎用DispatchとOnInputを使い、layoutや入力geometryを更新しない。IsRepeat=trueかつIsDown=falseはArgumentException、未知modifier bitはArgumentOutOfRangeExceptionとする。Key.Unknownはfalse。Keyの定義済み値を列挙して検査せず、それ以外の値はcustom handlerへ渡す。Button等が扱わないキーは未処理とし、将来の取得側のキー追加を定義検査で拒否しない。

編集・IMEのfocus/session同期は[UI-0012](UI-0012-ime-and-text-editing.md)のPR 11で追加する。取得側がIMEのキー消費を判定し、UIへ渡した系列の途中取消にはUiKeyCancelInputを使う。確定EnterからButtonのOnClickを発火させない。

## 検討した代替案

キーDownだけでClickするとrepeatやキー押下中の取り消しを扱いにくい。pointerとキーが別々の押下状態を持つと一つのButtonが二重認識するため、共通ownerと対応Upまでの消費集合を使う。

## 結果と影響

キーボードだけで基本UIを操作できる。ゲーム側が入力を共有する場合はDown/Upの消費を揃えられる。一方、任意focus指定、空間ナビゲーション、modal、IME、アクセシビリティは別の契約が必要になる。

## 検証方針

- null、一候補、複数候補、逆巡回、候補の差し替え、背景・無効部分木の除外を確認する。
- Enter/SpaceのDown→Up、repeat、modifier、focus移動後のUp、無効化後のUpを確認する。
- キー押下中のOnClickの置換・解除とcallback実行中の自己置換が、pointerと同じタイミングで反映されることを確認する。
- Tab/activation keyのDown・repeat・Up、途中取消、FocusLostでの消費状態の解除を確認する。
- pointer/key競合、別ButtonへのTab移動、callback例外、取り外し時にClickが重複しないことを確認する。

### Widgetへ配送する共通経路

通常キーはFocusedWidgetを起点として所有親へOnInputで配送し、Unhandledの場合だけ親へ進む。key ownerがある系列は同じWidgetへ直接配送し、Up/Cancelの所有を先に解除してIsKeyCaptured=trueで通知する。focus移動・対象除外ではUiKeyCancelInputを送り、既に消費した物理系列は終端までゲームへ渡さない。focus変更は旧WidgetへUiFocusChangedInput(false)、新Widgetへtrueを直接通知する。通知中の同期再入と例外では、所有状態を先に変更するUI-0005の規則を使う。

Tab/Shift-Tabの標準巡回は、focused Widgetと親が未処理の場合のfallbackとする。TextBox等がTabをHandledにした場合に巡回を追加しない。ButtonのEnter/Space、単独owner、IsPressed、ClickはButtonのOnInputと取消処理へ実装する。UiSurfaceはWidgetを保持するfocus/key owner表と配送だけを管理し、Button/TextBox型やOnClickを参照しない。TextBoxは同じhookで移動・選択・編集commandを処理できる。

このPRの追加検証には、Button以外のfocusable Widgetへのキー配送、子での消費による親のactivation防止、Tabの処理とfallback、key capture中のfocus喪失・取消通知を含める。TextInputの文書編集とOS IME sessionはUI-0012で接続する。

## 実装・検証記録

2026-10-29、#81へ担当ADR・実装・テスト・README・keyboard-onlyサンプルを追加した。UI 178件がDebug/Release、Drawing2D 99件がReleaseで成功した。Tab巡回、clip外・0寸法、背景除外、一般Widgetのキーcaptureと文字入力、pointer/key競合、取消・focus通知の再入・例外・破棄を確認した。安定したcaptured key repeatとpointer Moveの内部managed割当0Bも検証した。

keyboard-onlyサンプルでTabによるfocusとSpace Down/UpによるClick一回・押下解除、既存pointer-onlyサンプルのClick一回を確認した。7パッケージをpackし、ProjectReferenceを使わないNuGet consumerでstatic factory、継承したFocusable引数、callback引数、一般Widgetのkey capture/repeat/Up、文字入力の配送、Buttonと取消を実行した。Input.Abstractions依存がパッケージへ含まれることも確認した。

Windows/Browserの実キー取得・DPI・OS focusは次のhost接続PR、TextBoxとIME文書/sessionは後続のTextEditing PRの範囲である。本PRのキー配送時間の比較測定は行っておらず、既存のUI-0005の時間測定を本PRの最新測定値とは扱わない。

### 共通の再構築基盤との接続

再構築の照合結果へfocusとkey captureを接続する。対応するButtonは押下状態とキー系列を保持して新しいcallbackを使用する。除去対象はCancelし、消費済みキーは物理Upまで保持する。

TryFocus(Widget?)は本PRから公開する。同じsurfaceの接続済みでfocus可能な要素だけを受け入れ、nullは解除、別surfaceはArgumentExceptionとする。幾何更新は行わず最新layoutを前提とする。TextBoxなど後続controlもこの共通APIを使用する。
