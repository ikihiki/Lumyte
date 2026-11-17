# Dialog・通知・recipes の公開例

通常の UiApplication / UseHeadless / UseDiagnostics と、明示 font の Parley を使います。編集接続は既存サンプルの決定的な backend を明示借用し、OS IME の代用品と混同しません。

```sh
dotnet run --project samples/Lumyte.Samples -c Release -- --sample ui-dialogs --backend headless --configuration Release -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
dotnet run --project build -c Release -- --target TestUiDialogs --configuration Release
dotnet run --project build -c Release -- --target TestUiDialogsAot --configuration Release
```

確認する範囲は、Dialog の Hosted fallback、通常の TextBox と preedit 終了、scope 内の Tab 移動、Escape の要求、モデル閉鎖後の focus 復帰、NativeRequired の事前失敗です。DialogContent は普通の内容部品であり、自分で modal を開始しません。

通知は所有者上で一回作った ToastController、借用した単調時計、通常の ToastHost を使います。二件の FIFO を受け付け、表示中だけの一秒期限と sticky 通知を検査し、通常 Button の input lease で閉じます。通知の admission 自体が編集 focus / preedit を奪わないことも確認します。

既存の診断操作を通して schema 1〜9 の snapshot 形と source-generated JSON metadata を検査します。新しい role / wire field を追加しないので version 10 は導入しません。新しい公開ラベル関係は読み取り用 metadata で、wire snapshot に追加されたと説明しません。

入力 backdrop は透明な遮蔽です。Window の drag / resize 装飾、Native OS の activation・owner 抑止、Browser の実提示、物理 OS IME、GPU と他 RID は別の実環境の受け入れを要します。[UI-0036](../../docs/adr/ui/UI-0036-dialog-toast-and-recipes.md)を契約の正本とします。
