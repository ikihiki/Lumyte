# Lumyte.UI.Application

CompositionのUIとサービスを登録して起動する標準ホストです。入力、layout、全体描画、Window/Popup、IME、終了順序を管理します。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using static Lumyte.UI.Compose;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseRoot(context => Button(width: 160, height: 48, onClick: context.RequestStop));
await using var app = builder.Build();
await app.RunAsync();
```

`Build`はサービス登録を固定しDIを検証します。OS/GPUやroot factoryの呼び出しは`RunAsync`で開始します。Browserでは`UseBrowser`を使いJS threadから起動します。HeadlessはGPU/OS入力を持ちません。

`Configure(new UiApplicationOptions { FrameInterval = TimeSpan.FromSeconds(1d / 120) })`で120Hzを要求できます。
標準のthread loopは固定の描画期限を保ち、継続処理や停止要求で待機を解除します。
Windows Desktopでは高精度waitable timerを使い、利用できない場合は通常の待機へ戻ります。
描画中のrootへ重ねて提出せず、GPU完了と提示資源の解放後に次のframeを進めます。
実際の達成率はUIの負荷・device・OSのスケジューリングに依存します。

複数Windowは`using static Lumyte.UI.Application.Compose;`の`Window(...)`を`UseWindows`から返します。同じkeyの内容と内部状態を保持し、閉じて再追加した場合は新しいIDを発行します。`isOpen`はモデルで管理し、`onCloseRequested`でモデルを変更して`UiApplicationContext.RequestRebuildWindows()`を呼びます。AutomaticはNativeが利用できれば独立表示、それ以外はHosted、Hostedは同じ提示root内、NativeRequiredは非対応時に失敗します。

構築関数の再評価は`context.View.CurrentSurface.Rebuild(newRoot)`で公開します。変更済みWidgetを使い回す必要はありません。ページ/Signal/アニメーションの自動接続は後続packageで追加します。

TextBoxはstringとonTextChangedを受け取ります。`UseTextInput`へfont設定済みのIUiTextLayoutProviderを登録すると、hostが対応するWindows TSF/Browser textareaを接続します。OS serviceを各TextBoxへ渡しません。interface型providerのComposition引数は`layoutProvider: new(context.TextLayoutProvider)`と指定します。Linux/Headlessの編集backendは初期対応に含みません。候補表示の方針は`UiApplicationOptions.ImeCandidates`で指定します。

`context.Automation`は通常入力と同じ経路のUiAutomationPageへ渡せます。Popupは`context.PopupOwner`で開き、開いたIDから`GetPopupAutomation(id)`を取得します。Desktop/BrowserのPNG取得は要求時に実際の合成フレームをreadbackし、その対象の可視領域を切り出します。提示imageのCopySourceまたはRGBA8 sRGB中間画像のreadbackが利用できない場合はUnsupportedを返します。

Diagnosticsは別packageの`UseDiagnostics`で追加します。入力・テキスト入力・値の閲覧・画像取得の許可はそれぞれ既定falseです。通信は既存のAgent/transportを明示登録した場合だけ開始します。

独自contentは`IUiApplicationContent`で初期化・Advance・停止・Retire・Releaseを実装できます。GPU資源を借用する内容は`IUiApplicationGraphicsContent`で世代更新を確認します。Retire失敗は資源を保持するStopFailedとなり`RetryStopAsync`で再試行できます。最終Release/DI解放の失敗は再実行しません。

詳細と後続PRの範囲は[ADR UI-0016](../../../docs/adr/ui/UI-0016-ui-application-framework.md)を参照してください。
