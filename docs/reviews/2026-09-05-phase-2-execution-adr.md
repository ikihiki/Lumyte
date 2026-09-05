# E2 ADR: GPU実行・寿命・RenderGraphの契約

状態: 採用。U01/U09の契約をE2で固定する。公開入口の統合はE2b、非同期completionと所有権の拡張はE4。

## 提出と待機

- SubmitはCPU上の記録をGPU queueへ提出し、completionを返す操作とする。戻った時点ではGPU完了を保証しない。
- ExecuteAndWaitは提出と同期的な完了待機を明示する名前とする。
- WaitForCompletionAsyncはCPU threadを占有せず完了を待つ契約とする。CancellationTokenは呼び出し側の待機を取り消すだけで、GPU処理を取り消さず、資源も解放しない。
- E4で `Submit`、`ExecuteAndWait`、`WaitForCompletionAsync` を公開した。互換APIの `Execute` と `ExecuteAsync` は維持する。in-flight上限に達した提出は、空きが生じるまで同期的に待つ。
- `WaitForCompletionAsync` は `Task.Run` で同期Waitを包まず、completionをpollしてCPU threadを占有しない。

## 寿命

記録中のcommand bufferは呼び出し側が所有する。未提出のDisposeはAbortであり、GPUを実行せず、記録専用の資源を戻す。提出に成功するとqueueが記録を保持し、completion後にnative command bufferと記録専用資源を解放する。

失敗が提出前ならAbort可能、提出後ならcompletionの追跡を保持する。GpuSubmissionException.Completionで提出済み処理を待てる。device lostが確定した場合は正常完了とは区別して追跡資源を解放する。

commandはparameter dataを所有・uploadしない。大きなshader入力は通常のresource-table bufferとして呼び出し側またはRenderGraphが管理し、root data内のelement index/byte offsetからshaderが参照位置を算出する。

借用したtexture、view、pipeline、resource tableの資源自体は引き続き呼び出し側がGPU完了まで保持する。command-owned inputの保持を、外部資源全般の自動lease取得と混同しない。

## RenderGraph

- Readは既存内容を読む。先行writerが必要。
- Writeは先行内容を破棄する完全な上書き。attachmentのClear/Discard等に用いる。
- ReadWriteは先行内容を維持しつつ更新する。Load、部分書き込み、前の内容に依存するblendにはこれを用いる。更新するpixelが一部であってもWriteとはしない。
- passでtexture/bufferを使う場合、stageとaccessを宣言する。root dataから参照する外部bufferをtable経由で渡す場合もRead依存が必要。
- root dataから参照するbufferも通常のgraph resourceとしてRead/ReadWriteを宣言する。command内部に暗黙のparameter bufferや依存は作らない。
- Record(queue)にはbackend/resolverがない。import済み資源であっても新しいviewの生成はできない。借用viewを含むbindingか、backendを渡すExecute経路を用いる。contextを持つ記録入口への統合はE2b。
- exportは外部へ取り出す資源を特定する。名前は診断情報であり所有権の移譲を暗黙に意味しない。現在のexecutionが保持する資源はexecutionの寿命に従う。名前を伴う依存・寿命診断の強化はE6。

## 標準利用

AddDrawはWorld × ViewProjectionをCPUで算出し、row-major Matrix4x4を64-byte root dataとして渡す。shaderは合成行列を一度適用する。

共通consumerはbackend生成・presentation/readbackのhost接続以外にbackend分岐を置かず、同じshader package、binding、描画・computeコマンドを使う。Browserでのcompletionやcanvas接続の完成はE7の条件であり、このdesktop conformanceからBrowser対応を主張しない。
