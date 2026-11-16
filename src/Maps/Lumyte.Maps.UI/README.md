# MapViewのUI接続

`MapView`は借用する`MapSession`を必須とする受動的なWidgetです。宣言時にI/Oを開始しません。`MapElement`は既存UIのレイアウト、mount／unmount、ローカルの`OnRecord`、無効化、保持型描画の依存検証hookを使います。レイアウトは論理viewportの需要だけを更新するため、content側で`Advance`を呼んでください。

一つのsessionを同時にmountできるのは一か所です。unmountでは購読と接続を解除しますが、sessionを解放しません。`BeginStop`とGPU完了後の非同期退役はcontentが所有します。同じmount済み識別子でsessionを交換する操作は公開前に拒否します。保持cacheの再利用単位はElementのローカル記録であり、タイル単位のGPU差分更新ではありません。

この接続部はCanvasのBegin／End、準備、送信、待機、Presentを実行しません。ローカルclipとCanvas状態の復元はsessionの記録処理が担当します。公開アプリAPIとの統合は[sample](../../../samples/Lumyte.Maps.Sample/README.md)、記録のみの試験と実GPU画素試験の区別は[検証手順](../../../tests/Maps/README.md)を参照してください。
