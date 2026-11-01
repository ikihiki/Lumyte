# ADR-INPUT-0007: 取得時のWindow出所とデバイスを跨ぐ入力順序

- 状態: 採用
- 日付: 2026-10-28
- 対応: UI実装PR 6。INPUT-0001〜0005の取得・状態・加工・診断契約を拡張する

## 背景

現在のInputDataとInputRecordにはWindowの出所がない。PlatformはOS/DOMイベントの取得時点では対象Windowを知っているが、keyboard/mouseのdevice別queueへ入れる際にその情報を失う。InputSystemのSequenceとRecordedAtも取り込み時に付くため、後から現在のactive Windowや時刻を使って取得順を復元できない。

複数Windowや非活性Popupを扱うには、取得元Windowと、押下開始時の所有先、UIが選ぶHosted/surfaceを区別する必要がある。Windowごとに物理deviceを複製したり、FocusData(false)で同じdeviceの全Windowを解除したりする方法では、跨Windowの終端や別Windowの押下が壊れる。

## 決定

### 依存方向と適用範囲

取得元の識別子・順序付き取得契約はBCLのみのLumyte.Input.Abstractionsへ置く。[INPUT-0005](INPUT-0005-input-abstractions.md)の依存境界を維持し、InputからPlatform、Windowing、UIへ依存しない。実Windowとの対応は、既にInput.AbstractionsとWindowing.Abstractionsを参照するPlatform.Abstractionsへ置く。

既存IInputDevice.DrainEventsはWindow情報を持たない取得経路として維持する。標準Platformとその加工wrapperにはsource全体の順序付き取得を追加する。InputSystem、Processing、Actions、Input.Diagnostics、Windows/Linux/Browserの取得、利用例を同じ実装範囲で更新し、recordだけを変更して複数Window対応済みとしない。旧ADR全体を置換せず、本ADRが明示するWindow付き経路の差分以外は既存契約を継続する。

### ID・取得列の公開境界

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。InputData、InputDeviceId、IInputSource、IInputDeviceRegistryは既存型を使う。

```diff
+namespace Lumyte.Input.Abstractions
+{
+    // Windowの一回の接続寿命。Guid.Emptyは無効。OS handleやWindowing型を含まない。
+    public readonly record struct InputWindowId(Guid Value);
+    // OS/DOM取得時の因果識別。記録Sequence・frame番号・時刻とは別。
+    public readonly record struct InputCaptureStamp(Guid StreamId, ulong Sequence);
+    // WindowId=nullはWindowに結び付かない入力または出所不明。推測で補完しない。
+    // CaptureSequenceは物理取得なら正、時間進行等の合成出力ならnull。
+    public sealed record InputSourceEvent(InputDeviceId DeviceId, InputWindowId? WindowId,
+        ulong? CaptureSequence, InputData Data)
+    {
+        // 派生出力の直接の原因device。直接取得ではnull。
+        public InputDeviceId? OriginDeviceId { get; init; }
+        // OriginDeviceIdがある場合の加工scope。nullは明示Windowless。
+        public InputWindowId? StateWindowId { get; init; }
+    }
+    public sealed class InputEventBatch
+    {
+        // 配列を検証・コピーする。idは正、capturedThroughは取得済み末尾。
+        // Events=0もack対象。全件filterや編集scope消費後の取得境界を表せる。
+        public InputEventBatch(ulong id, ulong capturedThrough, IReadOnlyList<InputSourceEvent> events);
+        public ulong Id { get; }
+        public ulong CapturedThrough { get; }
+        public IReadOnlyList<InputSourceEvent> Events { get; }
+    }
+    public interface IOrderedInputSource : IInputSource
+    {
+        // 接続寿命中不変・非空。wrapperは内側の値を維持する。
+        Guid StreamId { get; }
+        // owner thread。未ackの先頭batchを不変で返す。batchなしならnull。
+        // Events=0のbatchと区別する。読み取りで消費しない。
+        InputEventBatch? PeekEvents();
+        // 検証済み先頭batchのackはallocation・利用者callbackなしで成功する。
+        // 直前のackの再指定は冪等、別ID/先頭飛ばしは変更前にArgumentException。
+        void CommitEvents(ulong batchId);
+        // 終端停止。取得・discoveryを止め未ack列を破棄する。再開不可、冪等。
+        // InputSystemへ接続中はInputSystem.StopSourceからだけ呼ぶ。
+        void AbortEvents();
+    }
+    // Window接続終了。WindowId必須。当該device/Windowの所有状態だけを取消す。
+    // WindowオブジェクトをDisposeする要求ではない。正常Upを生成しない。
+    public sealed record WindowInputClosedData : InputData;
+    // capture喪失等によるbutton単位の取消。位置不要、他button/keyを維持する。
+    // 未定義enumは取り込み前に拒否。解放済みなら状態変更なし。
+    public sealed record MouseButtonCanceledData(MouseButton Button) : InputData;
+}
+namespace Lumyte.Platform.Abstractions
+{
+    public interface IWindowInputSource : IOrderedInputSource
+    {
+        // owner thread。自分のPlatformの生存中IWindow.Idだけを解決する。
+        // 未対応・別Platform・終了済みはfalse。Window/sourceを所有・生成しない。
+        bool TryGetInputWindowId(WindowId windowId, out InputWindowId inputWindowId);
+    }
+}
```

PlatformはWindowの接続時に新しいInputWindowIdを割り当て、最初の入力より前にmapを公開する。OS handleの再利用、Window再生成、sourceの再接続では新IDにし、過去のIDを新しいWindowへ結び直さない。Hostedには別の取得Windowを作らず、提示元Native/canvasのIDを使う。Window終了時は各deviceのWindowInputClosedDataを取得列へ置き、mapを退役させる。未ackの終了記録とtombstoneは値だけを保持し、破棄済みWindowへの参照を残さない。

IPlatform.CreateInputSourceの戻り型は変更しない。Windows/Linux/Browserの標準sourceはIWindowInputSourceを実装する。UI hostは生成元のIWindowInputSourceをmap用に別途借用し、加工後のIOrderedInputSourceとStreamIdが一致することを検査する。Processingはordered capabilityだけを転送し、Platform.Abstractionsへ依存しない。wrapperはraw sourceを借用する。構成側はInputSystem終了・wrapper切離しまでraw sourceを維持し、その後に各sourceを既存の所有権に従ってDisposeする。借用mapを重ねてDisposeしない。

識別子は認証や操作権を表さない。Window不明のlegacy source、Windowに属さないcontroller、独立した合成入力のnullはUIのbroadcastや現在focusへの配送を意味しない。

### デバイスを跨ぐ順序とcommit

同じPlatform pumpのkeyboard/mouse/touch/penは一つのStreamIdと取得列を共有する。OS/DOM callbackでWindowIdと正のCaptureSequenceを固定し、device別queueへ分配してから並べ直さない。pointerの位置とbutton等、一つの取得を複数dataへ展開するときは同じCaptureSequenceを使い、配列内の連続した順序を保つ。加工で派生した出力も原因のstampを保つ。**処理順の正本はbatch内の配列順**であり、同じstampの派生を再sortしない。

InputSystemはsourceがIOrderedInputSourceなら、そのsourceのdevice.DrainEventsを呼ばずPeekEventsだけを取得する。同じStreamIdのsourceを重複登録しない。独立したsource間のOS全順序は保証せず、constructorへのsource注入順で取り込む。同じUI入力scopeのkeyboard/pointerを別streamへ分割する構成には、取得側の共通coordinatorを要求する。

1. Source.Updateを進め、先頭batchを取得する。device、WindowId、CaptureSequence、batch ID、型・値、所有系列の遷移を変更前に検証する。
2. batch全体を加工し、関係する全device/Window状態を一時コピーへ反映する。最終InputRecordのSequence・RecordedAtを配列順に確定する準備まで行う。
3. 追加確保やcallbackのない区間で状態・履歴をcommitし、CommitEventsでackする。その後にRecorded/inspection通知を配列順で行う。通知例外で再送・再ack・再認識しない。

未ackのbatchを次のbatchが追い越さない。加工失敗は同じbatch・加工時計・完了済みstageを保持して再試行し、そのstreamの他deviceも先へ進めない。別streamは従来どおり処理を続ける。これは[INPUT-0001](INPUT-0001-input-system.md)のdevice単位の部分成功と、[INPUT-0002](INPUT-0002-processing-and-recognition.md)の再試行を、順序付きsource内だけ一列へ拡張する変更である。

deviceの接続・切断登録は取得列のack済み境界で行う。先行入力が残る間はregistry変更を遅延し、接続待ちdeviceの後続入力も内部queueへ保持する。UnregisterDeviceをその境界で要求すると、InputSystemが取消・Disconnectedを記録してから登録と所有資源を解放する。sourceがDisconnectedを別に生成せず、InputSystemの接続・切断・中立化記録を正常入力の間へdevice登録順で割り込ませない。

PeekEventsの不正な値・逆行・別sourceのDeviceIdは当該batchをcommitしない。加工の再試行と、sourceの契約違反による停止を区別する。永久失敗・queue overflow・ホスト終了ではStopSourceを使用し、未ack列を黙って成功扱いにしない。標準sourceは1 batch最大4096項目、未処理全体最大16384項目とし、超過時は新規取得の公開を止めてfaultを報告する。物理終端とOS capture清掃は停止中も行う。

### InputRecord・状態・履歴

```diff
 namespace Lumyte.Input
 {
     public readonly record struct InputRecord(InputDeviceId DeviceId, ulong Sequence,
         TimeSpan RecordedAt, InputData Data)
     {
+        // 元イベントの取得Window。加工・保存・再読で現在Windowへ変更しない。
+        public InputWindowId? WindowId { get; init; }
+        // InputSystemが確定する状態owner/取消対象。HostedやUI focus先ではない。
+        public InputWindowId? StateWindowId { get; init; }
+        public InputCaptureStamp? Capture { get; init; }
     }
     public sealed class InputSystem
     {
+        // 当該device/Windowだけの不変snapshot。nullはWindowless scope。
+        public InputDeviceState GetState(InputDeviceId device, InputWindowId? windowId);
+        // ownerの更新外から呼ぶ。source不正・終了時に未ack列を破棄して停止する。
+        // 自分に登録したsource以外はArgumentException。停止済みは冪等。
+        public void StopSource(IOrderedInputSource source);
     }
     public sealed class InputDeviceState
     {
+        // MousePositionの座標空間。最後の取得originでありfocus先ではない。
+        public InputWindowId? MousePositionWindowId { get; }
+        public IReadOnlyDictionary<TouchContactId, InputWindowId?> TouchPositionWindows { get; }
+        public IReadOnlyDictionary<PenPointerId, InputWindowId?> PenPositionWindows { get; }
     }
     public sealed class InputReadResult
     {
+        // afterSequence以後に未記録の取得列を停止・破棄した。retentionのHasGapとは別。
+        public bool HasAcquisitionGap { get; }
     }
 }
```

物理deviceは引き続き一つのInputDeviceIdで登録する。内部状態をdevice/Window scopeへ分け、押下ownerを`(DeviceId, key/button/contact)`ごとに保持する。Down/BeganでStateWindowIdを固定し、別originからのMove/Up/Cancelでもそのownerだけへ適用する。WindowIdと座標は取得元のまま残し、Input内で座標変換しない。pointerのsnapshotは位置に対応するWindowIdも公開し、別Windowの座標を同じ空間とみなさない。

mouseは取得側captureと同じく、最初のbutton Downから全button終端までdevice内で一つのowner Windowを共有する。追加buttonのDownもそのownerへ属し、押下中のMouseMoveのStateWindowIdも一意に決まる。全button解放後のMove、wheel、所有系列のない値入力は取得originのscopeを使う。touch/penはcontactごと、keyboardはkeyごとにownerを持つ。物理系列のowner解決は診断override前の入力で行い、overrideがDown/Up値を変えても物理終端を延命しない。

FocusData(false)はそのrecordのWindow scopeだけを中立化し、他Windowの押下やrecognizerを解除しない。WindowInputClosedDataはそのscopeを退役させ、後着入力を新scopeとして復活させない。失ったscopeの物理押下は終端までtombstoneで消費し、新WindowへDownを捏造しない。DeviceDisconnectedDataと明示StopSourceだけは、そのdeviceまたはsourceの全scopeを終了させる。StopSourceは既存ownerを発火なしで取消し、Disconnectedと取得欠落を記録してからAbortEventsへ進み、別sourceの状態を維持する。停止したsourceは再開せず、再接続は新しいstream/device IDを使用する。

Window退役はstream内のIDとしても保持し、後から登録したdeviceが古いWindowIdを新scopeとして復活させることを拒否する。GetState(device, windowId)は未観測scopeなら中立snapshot、退役scopeなら終了時に中立化したsnapshotを返し、未知device・Guid.Emptyは拒否する。

StopSourceはそのsourceを以後のUpdate/Peekから除外し、取消・欠落・Disconnectedを一度だけ確定する。AbortEventsの例外でも他device/sourceの清掃を続け、例外を集約して呼び出し元へ報告する。終端清掃後のdevice/sourceのDisposeは既存所有権に従う。

Window付きpointerは、Windowにkeyboard focusがないという理由だけで捨てない。PreserveFocusのPopupもpointerを受理できる。keyboardのFocusDataとアプリ全体の非活性化を区別し、後者ではPlatformが影響する各Window scopeの取消を取得時に記録する。Windowlessの旧経路は従来のfocus規則を維持する。captureだけの喪失にはMouseButtonCanceledDataを使い、keyboard focusを変更しない。

既存GetState(device)は全scopeの集約snapshotを返す。IsDownはscope間のOR、IsFocusedはいずれかのfocus、接続状態はdevice単位とする。MousePositionは最後に観測した位置とMousePositionWindowIdの組、touch/penはdevice全体で一意なcontact IDと位置Windowの組を返す。操作対象の状態判断には明示したGetState(device, windowId)を使い、集約からWindowを推定しない。

Recorded、GetRecords、ReadRecordsとretentionは追加metadataを同じ値で保存する。SequenceはInputSystem全体の記録順、RecordedAtは既存の取り込み単調時刻、Captureは物理取得の因果識別として区別する。履歴のWindowIdだけでフィルターしてから終端を認識すると跨WindowのUpを失うため、所有系列は全記録から解決し、scope別状態・ActionsはStateWindowIdを使う。正常Upと取消の型を保存・再生で交換しない。

### 加工・仮想化・Actions

順序付き経路には次の明示型を追加する。既存sealed classのCorrectingInputSource/VirtualizingInputSourceはlegacy経路を維持し、IOrderedInputSourceをinnerへ渡した場合は構築時にArgumentExceptionで拒否する。インスタンスごとにinterfaceの有無を変えたり、標準sourceのmetadataを旧DrainEventsへ落として処理したりしない。

```diff
+namespace Lumyte.Input.Processing
+{
+    public sealed class OrderedCorrectingInputSource : IOrderedInputSource, IInputDeviceRegistry
+    {
+        // innerは借用。factoryはdevice/Windowごとに独立した加工器を返す。
+        public OrderedCorrectingInputSource(IOrderedInputSource inner,
+            Func<InputDeviceDescriptor, InputWindowId?, IEnumerable<IDeviceDataProcessor>> factory,
+            Func<TimeSpan> getTime);
+        // 全scopeの置換を準備し、未ack列の後の境界で一括適用する。
+        public void SetProcessorFactory(
+            Func<InputDeviceDescriptor, InputWindowId?, IEnumerable<IDeviceDataProcessor>> factory);
+        public Guid StreamId { get; }
+        public void Initialize(IInputDeviceRegistry registry);
+        public void Update();
+        public InputEventBatch? PeekEvents();
+        public void CommitEvents(ulong batchId);
+        public void AbortEvents();
+        public void Shutdown();
+        public void Dispose();
+        public InputDeviceId RegisterDevice(IInputDevice device);
+        public void UnregisterDevice(InputDeviceId device);
+    }
+    public sealed class OrderedVirtualizingInputSource : IOrderedInputSource, IInputDeviceRegistry
+    {
+        // nullのgeneratorは当該scopeから仮想出力を生成しない。
+        public OrderedVirtualizingInputSource(IOrderedInputSource inner,
+            Func<InputDeviceDescriptor, InputWindowId?, IVirtualDeviceGenerator?> factory,
+            Func<TimeSpan> getTime);
+        public void SetGeneratorFactory(
+            Func<InputDeviceDescriptor, InputWindowId?, IVirtualDeviceGenerator?> factory);
+        public Guid StreamId { get; }
+        public void Initialize(IInputDeviceRegistry registry);
+        public void Update();
+        public InputEventBatch? PeekEvents();
+        public void CommitEvents(ulong batchId);
+        public void AbortEvents();
+        public void Shutdown();
+        public void Dispose();
+        public InputDeviceId RegisterDevice(IInputDevice device);
+        public void UnregisterDevice(InputDeviceId device);
+    }
+}
```

新wrapperは同じStreamIdと取得順を維持し、加工後の不変な先頭batchを保持する。外側のbatch IDはwrapper内で採番し、対応する内側の先頭batch IDへ結び付け、外側のack後にだけ内側をackする。全件filterした場合もEvents=0のbatchを公開して同じ境界を維持する。内側にbatchがない時間進行だけの出力は外側のbatchとして公開し、そのackではinner.CommitEventsを呼ばない。CapturedThroughは内側で観測済みの値を維持し、内側に未処理入力がある間は時間進行の出力を先行させない。

物理deviceのWindow別複製は作らず、加工器・gesture状態だけをdevice/Windowごとに分離する。OrderedVirtualizingInputSourceは物理deviceの登録境界で対応する仮想deviceも一件登録し、Window別の認識状態から同じ仮想deviceへ出力する。scopeのfactoryがnullを返しても登録は維持し、そのscopeから出力しない。最初のWindow付きbatchを未ackにしたまま派生deviceの登録待ちになる循環を作らない。元のWindowId/CaptureSequenceを通常の補正・派生で失わず、Windowを跨いだ入力を一つの裸のInputData batchへまとめない。

factoryのnull引数・重複した状態付きinstance・不正な戻り値は適用前に拒否する。SetProcessorFactory/SetGeneratorFactoryは生存中scopeの置換候補を全て準備し、一部の準備失敗では現行factory・列・状態を維持する。成功した置換は未ack列を旧加工器で処理し終えた境界で行い、旧scopeを発火なしで取消してから新instanceへ切り替える。新scopeにも新factoryを使う。Shutdownは新規取得を止めて残列を維持し、AbortEventsだけが停止・破棄する。双方ともinnerへ一度転送し、borrowed sourceをDisposeしない。

既存IDeviceDataProcessor/IVirtualDeviceGeneratorはscopeごとに独立したinstanceへ、取得列の各原因イベントを順番に渡す。wrapper内も同じ開始owner規則を投影し、Aで始めた系列のB由来Up/CancelをAのgesture状態へ戻す。元のWindowIdをownerへ書き換えない。座標・値の補正は取得originの座標空間を維持し、系列認識のscopeと区別する。同じ呼出の出力はその位置へ安定挿入し、元の取得metadataを付ける。物理と仮想の枝は既存の順を保ち、後続の物理イベントより前へ置く。factoryが同じ状態付きinstanceを複数scopeへ返した場合は構築を拒否する。Windowless経路の従来のbatch処理は維持する。

入力がなくてもUpdateで各scopeの平滑化・期限を進める。空入力からの出力はその加工scopeのWindowIdを持ち、CaptureSequence=nullとして取得済み入力の後へ置く。取消・終了の合成出力も対象scopeを明示し、物理取得のstampを捏造しない。座標変換を持たないTouchControllerGeneratorは跨Windowのcontactを別Windowの同じgestureとして継続せず、旧scopeを合成取消して物理終端まで消費する。物理入力そのものは元のoriginのまま記録する。

ActionsはWindowごとのActionSystemへ明示接続する。同じ物理deviceを再登録せず、全InputRecordを与えた上でStateWindowIdによって自分のscopeを選ぶ。入力元originによる事前filterは禁止する。DeviceDisconnected/StopSourceは影響する全scopeへ適用し、Window限定Focus/Cancelは一致するscopeだけを発火なしで中断する。

```diff
 namespace Lumyte.Input.Actions
 {
     public sealed class ActionSystem
     {
+        // windowIdは作成後不変。既存constructorはnull(Windowless scope)として動作する。
+        // 同一Windowのkey/pointerだけを組み合わせ、他Windowの修飾キーを流用しない。
+        public ActionSystem(ActionProfile profile, InputBufferOptions? bufferOptions,
+            IActionInspection? inspection, InputWindowId? windowId);
+        public InputWindowId? WindowId { get; }
     }
 }
 namespace Lumyte.Input
 {
+    // inspection前に、未commitのbatch内の先行Downも含めてownerを解決した値。
+    public sealed record InputScopedEvent(InputSourceEvent Input, InputWindowId? StateWindowId);
     public interface IInputInspection
     {
+        // 順序付きsourceでは旧Processでなくこちらを一回呼ぶ。既定実装は入力を返す。
+        // metadataと列順を維持し、override対象scope以外へ値を移さない。
+        IReadOnlyList<InputScopedEvent> ProcessEvents(InputSystem system, Guid streamId,
+            IReadOnlyList<InputScopedEvent> events, TimeSpan now) => events;
     }
 }
```

### 診断・記録と再生

InputSystemはinspection前にbatchの先頭から物理ownerを投影し、未commitのDownも含むStateWindowIdをInputScopedEventへ渡す。Input.DiagnosticsはProcessEventsでこのscopeを使う。置換値をnew KeyData等で生成しても元のDeviceId・WindowId・CaptureSequence・StateWindowId・controlを維持し、overrideの所有対象をdevice/control/Windowへ結び付ける。物理ownerとoverrideの論理所有を別に保持し、物理UpをoverrideでDownに置換しても物理系列を延長しない。

Window限定focus喪失・閉鎖で他Windowのoverrideを解放しない。合成追加はCaptureSequence=nullとし、要求時に明示した生存中scopeのWindowId/StateWindowIdを使う。現在active Windowを注入先の既定値にしない。InputSystemは戻った列のmetadata不変条件と追加対象の生存を再検証し、違反時はbatch全体をcommitしない。

CreateSnapshotのdevice一覧は物理device一件を維持し、各deviceへWindow別stateを追加する。ActionSystem targetにはWindowId、履歴表示にはwindowId/stateWindowId/capture.streamId/capture.sequence、位置には座標Windowを含める。GuidはD形式、UInt64はdecimal string、nullは出所なしとして閉じたJSONへ固定し、既存の総サイズ上限とdetached値の送信を維持する。Input診断subsystemのschemaを2へ上げ、旧画面へWindow別対象を黙ってdevice単位で返さない。Observe/OverrideInput等の既存認証は変更しない。

[INPUT-0004](INPUT-0004-diagnostics-and-overrides.md)の未実装の記録・合成・再生範囲も、実装時に同じmetadataを保持する。再生では保存したWindow IDから生存中InputWindowIdへの明示mapを要求し、元のrecordは保持したまま新しいSequenceで合成する。古いCaptureStampを実OSの新しい取得と偽らず、再生の因果情報として別に保持する。map不明・終了済みは開始前に拒否し、旧形式の出所なし記録を複数Windowへ自動振分けしない。診断命令のOrder、CaptureSequence、InputRecord.Sequenceは別の番号である。

### PlatformとUIへの接続・移行

WindowsはReceiveのWindowsWindow、X11は取得イベントとcapture owner、BrowserはDOM callbackのcanvas/proxy登録世代からWindowIdを確定する。keyboard/pointerを分ける前に同じ列へ入れ、Window再利用と遅れたcallbackを世代で拒否する。取得側capture中はsourceが実際に得た座標空間を記録し、他Windowの座標への換算に失敗したら正常Upを作らず元の所有系列を取消す。

[UI-0007](../ui/UI-0007-window-input-and-dpi-integration.md)のrouterはmapで取得元の提示rootを選び、そこからHostedのhit testや保持中captureへ配送する。InputWindowIdはUIのfocus、Menu scope、UiSurfaceのIDではない。UI配送でWindowIdを書き換えず、実入力の取得元と配送先を併記できるようにする。

取得列の順序保証だけでは「pointer→文字」の先行編集を防げない。focusを変え得るpointer/keyを取得したら、後続文字をTSF/DOMが編集する前にその列のprefixをcommit・配送し、論理focus/sessionを同期して次の取得へ進む。InputSystemはprefixのflushからOS pumpを再入呼出ししない。同期できない再入中に新editorへ文字を推測配送することもない。

移行は次の単位で行う。

1. AbstractionsのID/envelope/ordered sourceとInputSystemの状態・履歴を追加する。旧constructorとdevice DrainEventsはWindowlessとして維持する。
2. 標準PlatformとProcessing/Diagnostics wrapperを同時に順序付き経路へ移す。旧加工wrapperをOrderedCorrectingInputSource/OrderedVirtualizingInputSourceへ替え、scope付きfactoryを使う。wrapperのordered capabilityとraw sourceのWindow mapを別途借用してStreamIdを照合し、どちらかが失われる構成は複数Window対応として登録しない。
3. UI routerはWindowIdとmapを必須にし、各Window用ActionSystemとscope状態照会へ利用側を移す。単一Windowでもlegacy入力を後段でそのWindowへ付け替えず、独自sourceは取得時にstampする。
4. Windows/Linuxの複数Native、Browserの単一canvasとproxyを実機確認する。サンプルの単一Window制限と、未検証backendの能力表示を対応結果に合わせる。

## 検討した代替案

- DeviceIdまたは取り込み時のactive Windowから出所を推定すると、複数Windowとframe内のfocus変更を復元できない。
- IWindow/WindowIdをInput.Abstractionsへ持ち込むと取得契約がWindowingへ依存する。opaque IDとPlatform側mapへ分ける。
- Windowごとに物理deviceを複製すると、device identity、跨Window終端、診断overrideの所有が分岐する。
- 各deviceのbatchを時刻やCaptureSequenceだけでsortしても、加工の再試行から遅れて戻る古いbatchが後続入力を追い越す。source全体のack境界を使う。
- すべてのsourceのOS全順序を一つの時計で保証すると、独立pump・remote/合成source間の同期を常に要求する。同じ取得stream内だけを保証する。

## 結果と影響

イベント時点のWindow、物理系列の状態owner、後段のUI配送先を区別し、履歴・加工・診断を通して維持できる。非活性Popupのpointerと元Windowのkeyboardを同じ物理device構成で扱える。

一方、順序付きsourceの一つの加工失敗は同じstreamの後続deviceも止める。scope別の状態・加工器と終了管理が増え、標準sourceへ移行するアプリはActionSystemのWindowを明示する必要がある。新規取得APIだけでOS captureやIMEの同期が完成するわけではなく、Platform/UI接続の受け入れ条件を合わせて満たす。

## 検証方針

- 一つのkeyboard/mouse deviceでWindow A/Bの交互入力を記録し、WindowId・StateWindowId・Capture・Recorded通知・履歴・状態が一致する。WindowやOS handleの再利用、遅いイベント、legacy/nullを現在Windowへ割り当てない。
- 同一pumpの「Aのkey→Bのpointer→Bのkey」「Down位置→button→key」「focus変更→文字」で取得順と配送順が一致する。device登録順を反転しても結果が変わらない。prefix flushはpumpを再入しない。
- 先頭batchの加工失敗中に同streamの別deviceが追い越さず、別streamは進む。再試行・ack・通知例外で一度だけcommitする。不正batch、overflow、StopSource、遅延disconnectを検証し、取得欠落と他sourceの状態を区別する。
- Aで開始したkey/button/contactがB由来のUp/CancelでAだけを終了する。Aのfocus喪失・Window終了がBの押下/recognizer/overrideを解除しない。非活性Native popupのpointer、アプリ非活性化、captureだけの喪失を区別する。
- 補正・仮想化の多段wrapper、同原因の複数出力、全件filterした空batch、入力なしの平滑化・期限、跨Windowのgesture取消でmetadataと列順を維持する。空batchのackと内側batchのない時間進行のackを区別する。同じ物理deviceをWindow数だけ登録しない。旧wrapperへのordered source注入を拒否し、scope付きfactoryの失敗・一括置換・借用sourceの終了順を確認する。
- WindowごとのActionSystemが他Windowの修飾キーを使わず、別originの終端をStateWindowIdで処理する。旧Windowless sourceとActionSystemの既存契約も維持する。
- Diagnostics snapshot・override・記録/再生のschema、Window map、権限、終了済みID、旧形式、取消型の保持を確認する。再生で古いSequenceや物理取得stampを新入力として再利用しない。
- Input.Abstractionsの依存閉包にWindowing/Platform/UIが入らず、Windows/Linux/Browserの取得、既存Input/Processing/Actions/Diagnosticsの回帰、trimming/Native AOTを対象環境で確認する。

## 実装上の具体化

Window付きの診断controlは`InputInspectionSession.GetWindowTarget`で選択する。pointerの位置・pressureには既存名のWindow引数付きoverloadを使う。Windowなしの集約targetをWindow付きdeviceへ適用しようとした場合は`window-required`で拒否する。snapshot schemaは2とし、直近32件/deviceの履歴に取得origin・状態owner・captureを表示する。deviceは複製せず、windowScopesに個別状態とtargetを記録する。

```diff
 namespace Lumyte.Input.Diagnostics
 {
     public sealed class InputInspectionSession
     {
+        // 観測済みで接続中の明示scope。未知・退役済み・切断済みはKeyNotFoundException。
+        public InputOverrideTarget GetWindowTarget(InputSystem system, InputDeviceId device,
+            InputWindowId window, Lumyte.Input.Actions.InputControl control);
+        public InputOverrideTarget GetMouseTarget(InputSystem system, InputDeviceId device,
+            InputWindowId window, bool wheel = false);
+        public InputOverrideTarget GetTouchTarget(InputSystem system, InputDeviceId device,
+            InputWindowId window, TouchContactId contact, bool pressure = false);
+        public InputOverrideTarget GetPenTarget(InputSystem system, InputDeviceId device,
+            InputWindowId window, PenPointerId pointer, bool pressure = false);
     }
 }
```

既存Tapeは取得レコードの再生ではなく、aliasと操作列を持つプログラムである。再生時にWindow付きtargetへ明示bindし、保存された取得Sequenceを再利用しない。取得列そのものの記録・Window map付き再生APIはこのPRでは追加しない。

標準Platformの現在の取得対象はkeyboard/mouseである。touch/penの順序・scope・位置metadata・UI変換はfixtureで検証し、OS/DOMでのtouch/pen取得は従来どおり未対応とする。X11接続喪失からの復旧は既存backendの制限を維持する。X11 capture取消後は観測した全buttonの終端まで再開せず、観測できない終端を推測しない。

派生出力では、取得originと認識scopeが一致しない場合がある。例えばAで始めたkeyのB由来Upから作る仮想controllerのUpは、WindowId=BのままStateWindowId=Aである。wrapperは共通の系列owner規則からこのscopeを確定し、OriginDeviceIdとともにInputSourceEventへ保持する。InputSystemは原因deviceの同一source所属と、同じ取得原因が列に残っている場合のscope一致を検証する。外側のwrapperが物理枝をfilterしても派生枝のscopeを失わない。直接取得のStateWindowId指定は拒否し、inspectionがこれらのmetadataを書き換えることも拒否する。時間進行・scope取消の合成出力は対象scopeをoriginとしてCaptureSequence=nullにする。
