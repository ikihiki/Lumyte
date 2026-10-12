# ADR-PLATFORM-0002: Clipboard とネイティブ文字入力サービス

- 状態: 採用
- 日付: 2026-10-11

## 背景

Clipboard と IME は単一ウインドウでも必要になる OS 機能であり、複数ウインドウ管理を必須にする理由にならない。Windows TSF はメッセージ処理に加えて COM の文書アクセス、ロック、選択範囲、画面上の入力位置への同期応答を必要とする。確定文字列の事後通知だけでは TSF に対応できない。

Browser の Clipboard は Promise と権限制御を使うため、共通契約を Windows の同期 API に限定しない。

## 決定

IPlatform.CreateClipboard と CreateTextInputService が共通契約を返す。契約は Lumyte.Platform.Abstractions に配置し、Windowing の管理実装、InputSystem、Graphics に依存しない。各サービス、文字入力セッションは生成した側が所有する。セッションとサービスを Window / Platform より先に解放する。

Clipboard は Unicode テキストの非同期読み書きと内容を含まない変更通知を提供する。Features は API の実装有無を示し、現在の権限や取得成功を保証しない。未対応の機能は利用側が確認でき、権限拒否・使用中・Native の失敗を別の形式や内部バッファで代替しない。

文字入力の文書と選択範囲は UI が保持し、バックエンドが ITextInputClient を通じて同期アクセスする。位置は UTF-16 のコード単位、範囲は start と length で表す。Replace は指定範囲を置換し、選択範囲を挿入末尾へ移す。変換途中のテキストも文書に含まれる。GetBounds は Native クライアントの物理ピクセルで範囲・キャレットの矩形とクリップ状態を返す。Hosted 編集領域では UI が親の配置と DPI を反映した座標へ変換する。

### 公開 API

比較元は main の `cc766f2`。以下は追加する主要宣言である。

```diff
+using Lumyte.Windowing.Abstractions;
+namespace Lumyte.Platform.Abstractions;
+
+[Flags]
+public enum ClipboardFeatures { None = 0, TextRead = 1, TextWrite = 2, ChangeNotifications = 4 }
+public interface IClipboard : IDisposable
+{
+    // 通知能力がある場合だけ発生。内容そのものは通知しない。
+    public event Action? Changed;
+    public ClipboardFeatures Features { get; }
+    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);
+    public ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default);
+}
+public readonly record struct TextSelection(int Start, int Length, bool ActiveAtStart = false);
+public readonly record struct TextInputBounds(WindowRect Rectangle, bool IsClipped);
+public interface ITextInputClient
+{
+    public string Text { get; }
+    public TextSelection Selection { get; }
+    public bool IsReadOnly { get; }
+    public void Replace(int start, int length, string text);
+    public void SetSelection(TextSelection selection);
+    public TextInputBounds GetBounds(int start, int length);
+}
+public interface ITextInputService : IDisposable
+{
+    // Window / Client は借用。同じ Window には一つのセッションまで。
+    public ITextInputSession CreateSession(IWindow window, ITextInputClient client);
+}
+public interface ITextInputSession : IDisposable
+{
+    public event Action<bool>? CompositionChanged;
+    public bool IsComposing { get; }
+    public bool UsesSystemCandidateUi { get; }
+    public void Focus();
+    public void Blur();
+    // アプリ側で行った文書変更だけを報告。Native が行った変更は再通知しない。
+    public void NotifyTextChanged(int start, int oldLength, int newLength);
+    public void NotifySelectionChanged();
+    public void NotifyLayoutChanged();
+}
```

### Windows TSF（後続実装）

今回の Windows backend は文字入力を実装しない。`PlatformFeatures.TextInput` を広告せず、`CreateTextInputService` は `NotSupportedException` を送出する。以下は後続の TSF 実装が満たす設計条件である。

WindowsPlatform は STA スレッドごとに一つとする。将来の WindowsTextInputService は ITfThreadMgr を Activate し、各セッションが ITfDocumentMgr と ITfContext、ITextStoreACP の実装を保持する。Focus が Window と文書を関連付け、OS のフォーカス通知に従って TSF の対象を更新する。

PumpEvents はキーメッセージを ITfKeystrokeMgr の TestKeyDown / TestKeyUp と KeyDown / KeyUp に渡す。TSF が消費したメッセージを TranslateMessage に渡さず、WM_CHAR と TSF 編集の二重適用を防ぐ。物理キーの Input Source への記録は文字入力による消費と独立する。通常の未消費の WM_CHAR は現在の編集セッションへ渡す。Backspace、矢印、Enter などの編集方針は UI が物理入力から処理する。

ITextStoreACP は read / read-write ロックを守る。ロック中の同期再要求には TS_E_SYNCHRONOUS、非同期再要求には TS_S_ASYNC を返し、後の Platform 処理でロックを付与する。文書への Native コールバック中に Platform / Window / セッションを変更できない。Client の失敗は HRESULT に変換し、Platform の例外集約にも報告する。

アプリ側の編集、選択移動、スクロール・DPI 変更は NotifyTextChanged / NotifySelectionChanged / NotifyLayoutChanged で TSF へ伝える。Native 編集中に同じ変更を再通知しない。Blur / Dispose は TSF の変換終了処理を呼び、現在の文書を維持する。独自のキャンセルや元文書の復元は本契約で保証しない。

将来の初期実装は OS 標準の候補 UI のみを使用し、UsesSystemCandidateUi は true とする。独自候補 UI 用の列挙・選択・標準表示抑止は含めない。Windowing の ImeCandidates は表示領域を管理できるが、それだけで TSF の候補情報を取得できるとは扱わない。リッチテキスト、埋め込みオブジェクト、位置から文字への逆ヒットテストも初期 TSF 実装には含めない。

### Clipboard の非同期処理

WindowsClipboard は同期 Win32 API から完了済み ValueTask を返す。Unicode 形式がなければ null、空文字列は空のテキストとして区別する。OpenClipboard が失敗した場合は IOException とし、明示的な再試行は利用側が判断する。書き込み用 GlobalAlloc の所有権は SetClipboardData 成功時だけ OS へ移転する。

BrowserClipboard は secure context の navigator.clipboard を使用する。権限拒否は Promise の失敗として伝える。OS の内容変更通知は未対応で、ChangeNotifications を広告しない。キャンセルは Native 提出前に確認する。読み取りは取消不能な Promise の終了後にキャンセルを確認し、書き込みは提出後の取消を成功していない書き込みとして報告しない。未完了の Native 処理がある間は Dispose を拒否する。

### 実装と対応範囲

| Backend | Clipboard | 文字入力 |
| --- | --- | --- |
| Windows | Unicode 読み書き、変更通知 | 未実装。TSF 対応は後続。factory は NotSupportedException |
| X11 | 未実装。factory は NotSupportedException | XIM は未実装。factory は NotSupportedException |
| Browser | 非同期 Unicode 読み書き。権限・secure context が必要 | DOM 編集・composition は未実装。factory は NotSupportedException |

共通契約の存在と各環境の実装状況を区別する。未対応サービスは PlatformFeatures に含めない。

## 検討した代替案

- IWindow に Clipboard / IME を含める: 文書と Window の寿命・責務が異なり、共通 Window 契約を肥大化するため採用しない。
- Windowing が TSF を管理する: 単一 Window でも管理実装が必須になるため採用しない。
- 確定文字列のみをキューで配送する: 文書ロックと同期照会に対応できない。
- Clipboard を同期 API のみにする: Browser の Promise と権限制御に適合しない。

## 結果と影響

単一 Window でも Platform の Clipboard を使用できる。文字入力の共通契約は backend 実装を追加するための境界を定めるが、今回の全 backend は未対応である。対応 backend を使う UI は編集文書と Native 用の同期アクセスを実装する必要がある。サービスの生成だけでは文字の描画や UI 編集機能を提供しない。

## 検証方針

Windows の Native テストで `TextInput` capability を広告せず、factory が `NotSupportedException` を送出することを確認する。TSF の文書ロック、UTF-16 編集、実際の IME 変換と候補位置は後続実装で検証する。

Browser の JS テストでは Clipboard の secure context 判定と権限エラー伝達を確認する。DOM / WebGPU / Clipboard の実ブラウザー統合動作をこの単体テストだけで検証済みとは扱わない。

## 参考資料

- [Platform の生成と更新](PLATFORM-0001-platform-and-window-contracts.md)
- [任意の Windowing 管理](../window/WINDOW-0001-window-system.md)
