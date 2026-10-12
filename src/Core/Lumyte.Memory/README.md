# Lumyte.Memory

リソース、Graphics、Audioに依存しないメモリ予算と所有権のコア機能です。

`MemoryManager`はpoolごとのsoft／hard limit、同時予約、確定済みcharge、見積もり量を追跡します。外部のGPU／Native資源は`ReserveAsync`、物理確保、`Commit`の順で接続し、物理資源を解放した後にchargeを返します。予測を超えた確保も台帳に残し、以後の予約を拒否します。

`RentAsync<T>`は`ArrayPool<T>`から借りた実capacityを計上し、要求長だけを`Memory`として公開します。bufferとscopeの解放は冪等です。scopeはDI scopeとは別の実行時寿命で、利用側が所有します。

回収は`ReclaimAsync`から明示的に開始します。予約失敗から自動的にreclaimerを呼びません。同じpoolの回収は直列化し、callback中の同poolへの予約を拒否します。返却量はreclaimerの申告値ではなく、このmanagerのchargeが実際に減った量です。

標準DIとの接続は`Lumyte.Memory.DependencyInjection`を使用します。
