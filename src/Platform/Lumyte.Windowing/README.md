# Lumyte.Windowing

複数の Native Window と、同じ提示先へ描画する Hosted 領域を管理する任意の .NET 10 ライブラリです。`IWindow` は `Lumyte.Windowing.Abstractions` の契約を各バックエンドが直接実装します。

単一 Window なら `IPlatform.CreateWindow` と `IWindow` だけを使用でき、このパッケージは不要です。

Hosted は UI 内容を描画しません。描画側は `WindowNode.Bounds`、`GetHostedOrder`、`PresentationRoot` を使い、親からの座標合成とクリップを適用します。入力の当たり判定には Native クライアント座標の `HitTest` を使用します。状態参照と操作は `WindowNode` に対して行い、内部管理 ID は公開しません。入力のイベント時点の配送先を保持する envelope は未実装です。

`Attach` は Owned / Borrowed を明示します。Borrowed の終了は利用側が担当し、`Detach` は元の Native を閉じません。Owned を閉じる前に Graphics の保持を解放してください。保持中の終了は `PumpEvents` / `Update` を継続して待ちます。System 自体は Platform を解放しません。

[設計、所有権、利用例](../../../docs/adr/window/WINDOW-0001-window-system.md#利用例)を参照してください。
