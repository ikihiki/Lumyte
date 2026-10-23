# Lumyte.Execution.Generators

コマンドを宣言する型は、元インスタンスへの参照一本を持つ`readonly partial struct`です。生成器はラッパー自身へ送信メソッドを追加します。

```csharp
public readonly partial struct AudioVoice(IAudioVoice original)
{
    private readonly IAudioVoice _original = original
        ?? throw new ArgumentNullException(nameof(original));

    [ExecutionCommand]
    private void PlayCore() => _original.Play();
}
```

```csharp
// モジュールのDeclareで一度だけ登録する。
AudioVoice.PlayWork play = AudioVoice.DeclarePlay(builder, options);

// originalは生成済みのbackendインスタンス。
var voice = new AudioVoice(original);
await voice.Play();
voice.AddPlayTo(batch);
```

`PlayCore`から`PlayCommand`、statelessなreadonly struct handler、静的な`DeclarePlay`、`PlayWork`、直接送信する`Play`と`AddPlayTo`を生成します。`Core`の接尾辞を外した名前が公開名です。別の公開名が必要なら`Name`を指定します。別のsender型、For、Port、Sender／UseReceiver／ExecutionTargetは提供しません。

ラッパーは元インスタンスを保持する明示的なreadonly参照fieldを一つだけ宣言します。value field、nullable参照、追加instance field、非readonly struct、classによるコマンド宣言はエラーです。defaultラッパーの送信は拒否します。元インスタンスの寿命や所属の確認はbackendへ任せます。

送信本体はtyped queueへのTryEnqueueとValueTask構築を展開します。ラムダ、async state machine、ExecuteAsyncへの転送、reflection、StackTraceHiddenを生成しません。handlerはcommandに含むラッパーのCoreメソッドを直接呼びます。元インスタンスごとの登録やID表は不要です。

返り値なしはUnitです。`Input = ExecutionInputKind.ExistingCommand`では単一の`in struct`入力を、ラッパー参照とともにtyped envelopeへ格納します。末尾の`in WorkContext`はpayloadに含めず注入します。

生成したWorkのlifecycle送信は`await work.ExecuteAsync(context, receiver, arguments...)`です。処理対象のラッパーを明示します。`AllowAwaitableResult = true`は同期handlerからpending Task／ValueTaskを返すStartup専用の宣言で、外側workflowが実operationを待ち、同じownerで結果を確定します。

`ExecutionUpdate`と`ExecutionRange`は更新／範囲handlerと登録APIを生成します。参照一本のラッパーまたは専用のpartial classへ宣言できます。Updateは`in UpdateTick`、Rangeは`(in TContext, int first, int endExclusive)`と対応する`int Length(in TContext)`を使います。

privateな同期・非virtual・非genericメソッドを定義してください。ref/out、pointer、ref-like、delegate、dynamic、Expression、optional／paramsなどはコマンド入力の対象外です。LMEXEC001–008で宣言・signature・名前衝突・入力型・属性・pending結果・ラッパーfield・runtime不一致を診断し、不正な型には一部だけのコードを生成しません。

analyzerとして参照します。

```xml
<ProjectReference Include="../Lumyte.Execution.Generators/Lumyte.Execution.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```
