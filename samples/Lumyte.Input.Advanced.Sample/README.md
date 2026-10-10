# 入力加工・アクション・設定保存サンプル

Composition で common コンテキスト内に jump と認識を定義し、game が継承して
バインディングを上書きします。保存する定義には親子関係を保持します。

実 OS 入力の代わりにデモ Source を使い、DI から InputSystem に接続します。

```sh
dotnet run --project samples/Lumyte.Input.Advanced.Sample -- /tmp/input-settings.json
```

J キーをリバインド候補として捕捉し、設定ファイルへ保存します。保存成功後の
次の更新で適用し、解放・再押下から jump-press を認識してバッファを一度消費
します。同じファイルを指定して再実行すると保存済みのキーを復元します。

InputTimeSource は InputSystem 構築後に接続するため、構築中の再帰的 DI 解決と
終了処理中のサービス再解決を避けられます。加工側はコンテナーに依存しません。

このコンソール例は保存完了を同期的に待ちます。UI は非同期保存し、保存後の
ApplyCommittedSettings を入力管理スレッドの次回更新に実行してください。

タッチバックエンドを接続すると同じ構成で仮想コントローラーを生成できます。
Advanced.Tests はタッチのスワイプ、ピンチ、回転、キャンセルも検証します。
