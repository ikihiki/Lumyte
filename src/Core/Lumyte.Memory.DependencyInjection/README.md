# Lumyte.Memory.DependencyInjection

`Lumyte.Memory`をMicrosoft.Extensions.DependencyInjectionへsingleton登録します。

```csharp
services.AddMemoryManagement(options =>
{
    options.ChunkSizeBytes = 64 * 1024;
    options.SetBudget(new MemoryPoolId("cpu"), 512 * 1024 * 1024, 768 * 1024 * 1024);
    options.SetBudget(new MemoryPoolId("gpu/main"), 1024 * 1024 * 1024, 1536 * 1024 * 1024);
});
```

未設定のpoolは無制限です。`ChunkSizeBytes`はスレッドごとのCPUチャンクの標準容量で、既定値は64 KiBです。設定値は登録時に固定します。`AddMemoryManagement<TManager>()`で同じ契約の実装へ交換できます。重複登録は構成エラーになります。
