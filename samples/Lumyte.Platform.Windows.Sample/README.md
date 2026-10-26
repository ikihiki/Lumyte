# Lumyte.Platform.Windows.Sample

Windowing 管理実装と Input 本体を参照しない、単一 Native Window のサンプルです。Windows 10 version 1809 以降で実行します。

```sh
dotnet run --project samples/Lumyte.Platform.Windows.Sample
```

`WindowsPlatform` から `IWindow` を直接生成し、表示、メッセージ処理、終了を行います。Graphics と接続するアプリでは `WindowsWindow` から HWND を取得し、GPU / Surface を Window より先に解放します。
