# Lumyte.Platform.Windows.Sample

Windowing 管理実装と Input 本体を参照しない、単一 Native Window / TSF のサンプルです。Windows 10 version 1809 以降で実行します。

```sh
dotnet run --project samples/Lumyte.Platform.Windows.Sample
```

Window にフォーカスして文字を入力すると、編集文書をコンソールへ出力します。IME の候補 UI は OS 標準表示です。文書の内容描画と Backspace・矢印などの編集キー処理はこのサンプルには含めません。GetBounds は候補位置のための固定の例示 geometry を返します。

終了時はテキストセッションを解放して保持トークンを解除し、Platform の処理を継続して Native の物理終了を完了させます。Graphics と接続するアプリでは GPU / Surface の解放も保持解除より先に行います。
