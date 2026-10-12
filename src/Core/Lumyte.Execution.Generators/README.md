# Lumyte.Execution.Generators

`Lumyte.Execution` 用のincremental source generator。netstandard2.0のAnalyzerとして配布し、Roslynはruntimeへ持ち込まない。

```xml
<ProjectReference Include="../Lumyte.Execution.Generators/Lumyte.Execution.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

namespace直下のpublic／internalな非generic partial classで、同期instanceメソッドへ属性を付ける。

```csharp
public sealed partial class AudioState
{
    [ExecutionCommand]
    private AudioVoice CreateVoice(int clip) => CreateOnOwner(clip);

    [ExecutionCommand(Sender = "Voice")]
    private void Play([ExecutionTarget] AudioVoice voice) => voice.Backend.Play();
}
```

`DeclareCreateVoice(builder, options)`、`CreateVoicePort.ExecuteAsync(...)`、`Voice.For(original).Play()` 等を生成する。Sender groupのDeclare引数はwork名のordinal順。返り値なしはUnit、既存commandには `Input = ExecutionInputKind.ExistingCommand` と単一の `in struct` 引数を使う。末尾の `in WorkContext` はpayloadへ入れず注入する。

Senderは対象classへの参照一本だけを保持する。登録時にownerとFIFO groupの一致を検査し、Startup、CPU、awaitable結果をgroupへ混在させない。default senderとFor(null)は拒否する。元対象の所属・native寿命検査はhandlerの責務。

`ExecutionUpdate` は `void Update(in UpdateTick)`、`ExecutionRange(nameof(Length))` は `void Execute(in TContext, int first, int endExclusive)` と `int Length(in TContext)` から直接呼ぶhandlerを生成する。RangeへのWorkContext注入は対応しない。Updateでは末尾WorkContextも使用できる。

同期メソッドがTask／ValueTask等のpending operationを返す場合は `AllowAwaitableResult = true` を指定し、Startupとして登録する。generatorはawaitやunwrapを行わず、実operationの追跡をlifecycle workflowへ残す。

生成senderは各static readonly endpointへのTryEnqueueとValueTask構築を直接展開する。ExecuteAsyncへの転送、async state machine、送信helper、StackTraceHidden、対象ごとのbindingやID lookupを生成しない。生成名・メンバー順・hintはsource pathやcultureへ依存しない。

LMEXEC001–008でcontainer／signature／名前衝突／型／属性／awaitable／target／runtime不一致を報告する。不正なclassには一部だけのコードを生成しない。privateな出力型、ref/out、pointer、ref-like、delegate、dynamic、Expression、optional／paramsなどは初期対象外。

[実行基盤](../Lumyte.Execution/README.md)と[動くAudioサンプル](../../../samples/Lumyte.Execution.Sample/README.md)を参照。

ラッパー自身を対象にする場合は、privateなインスタンスメソッドへ`UseReceiver = true`を指定します。

```csharp
public sealed partial class AudioVoice
{
    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Play() => Backend.Play();
}
```

`AudioVoice.DeclareCommands(builder, options)`は静的な登録メソッドになります。`AudioVoice.Commands.For(original).Play()`は元インスタンスをpayloadに保持し、statelessなreadonly struct handlerが`original.Play()`を直接呼びます。登録用のインスタンスやhandler用のclass allocationは不要です。`UseReceiver`にはSenderとPackArgumentsが必要で、同じgroupに明示的なExecutionTarget方式を混在させません。
