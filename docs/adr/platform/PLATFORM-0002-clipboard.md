# ADR-PLATFORM-0002: Clipboard サービス

- 状態: 採用
- 日付: 2026-10-11

## 背景

Clipboard は単一ウインドウでも必要になる OS 機能であり、複数ウインドウ管理を必須にする理由にならない。Browser の Clipboard は Promise と権限制御を使うため、共通契約を Windows の同期 API に限定しない。

## 決定

`IPlatform.CreateClipboard` が `Lumyte.Platform.Abstractions` の共通契約を返す。契約は Windowing の管理実装、InputSystem、Graphics に依存しない。サービスは生成した側が所有し、Window / Platform より先に解放する。

Clipboard は Unicode テキストの非同期読み書きと内容を含まない変更通知を提供する。Features は API の実装有無を示し、現在の権限や取得成功を保証しない。未対応の機能は利用側が確認でき、権限拒否・使用中・Native の失敗を別の形式や内部バッファで代替しない。

### 公開 API

比較元は main の `cc766f2`。以下は追加する主要宣言である。

```diff
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
```

### 非同期処理

WindowsClipboard は同期 Win32 API から完了済み ValueTask を返す。Unicode 形式がなければ null、空文字列は空のテキストとして区別する。OpenClipboard が失敗した場合は IOException とし、明示的な再試行は利用側が判断する。書き込み用 GlobalAlloc の所有権は SetClipboardData 成功時だけ OS へ移転する。

BrowserClipboard は secure context の navigator.clipboard を使用する。権限拒否は Promise の失敗として伝える。OS の内容変更通知は未対応で、ChangeNotifications を広告しない。キャンセルは Native 提出前に確認する。読み取りは取消不能な Promise の終了後にキャンセルを確認し、書き込みは提出後の取消を成功していない書き込みとして報告しない。未完了の Native 処理がある間は Dispose を拒否する。

### 実装と対応範囲

| Backend | Clipboard |
| --- | --- |
| Windows | Unicode 読み書き、変更通知 |
| X11 | 未実装。factory は NotSupportedException |
| Browser | 非同期 Unicode 読み書き。権限・secure context が必要 |

未対応 backend は `PlatformFeatures.Clipboard` を広告せず、factory は `NotSupportedException` を送出する。

## 検討した代替案

- IWindow に Clipboard を含める: Clipboard と Window の寿命・責務が異なり、共通 Window 契約を肥大化するため採用しない。
- Clipboard を同期 API のみにする: Browser の Promise と権限制御に適合しない。

## 結果と影響

単一 Window でも Platform の Clipboard を使用できる。Windows では作成 STA スレッド、Browser では対応する JS context に戻って利用する。

## 検証方針

Windows の Native テストで Unicode テキストの読み書きと変更通知を確認する。Browser の JS テストでは Clipboard の secure context 判定と権限エラー伝達を確認する。実ブラウザーの Clipboard 動作を単体テストだけで検証済みとは扱わない。

## 参考資料

- [Platform の生成と更新](PLATFORM-0001-platform-and-window-contracts.md)
- [任意の Windowing 管理](../window/WINDOW-0001-window-system.md)
