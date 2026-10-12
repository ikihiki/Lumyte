# Lumyte.Windowing.Windows.Sample

`WindowSystem` で複数の Native Window と Hosted 領域を管理する Windows 向けサンプルです。Windows 10 version 1809 以降で実行します。

```sh
dotnet run --project samples/Lumyte.Windowing.Windows.Sample
```

メイン Window、所有される Tool Window、メイン Window 内の Hosted コンテキストメニュー領域を生成します。操作と状態参照は、生成時に返される `WindowNode` に対して行います。利用側へ管理 ID は公開されません。

Hosted 領域は Native Window を増やさず、描画内容も持ちません。このサンプルは領域の管理と状態変更をコンソールへ出力します。実際のメニュー描画では、Framework が `Bounds`、`GetHostedOrder()`、`HitTest()` を Graphics と Input に接続します。
