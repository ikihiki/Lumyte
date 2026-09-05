export type ConnectionStage='idle'|'server-connecting'|'server-connected'|'negotiating'|'host-selecting'|'host-connected'|'incompatible'|'disconnected';
export interface ProtocolError {code:string;message:string;retryable:boolean;details?:string}
export interface Envelope<T=unknown>{id?:number;result?:T;error?:ProtocolError;event?:{subscriptionId:string;hostId:string;domain:string;feature:string;params:unknown}}
export interface HostInfo{hostId:string;displayName:string;connected:boolean}
export interface FeatureInfo{name:string;kind:'query'|'command'|'event';requestType:string;responseType?:string}
export interface DomainInfo{name:string;features:FeatureInfo[]}
export interface Negotiation{protocolVersion:string;supportedVersions:string[];capabilities:string[];heartbeatIntervalMilliseconds:number}
export interface InputSource{source:string;deviceId:string;pressedKeys:string[];pressedMouseButtons:string[];pointerPosition:Record<string,number>;pointerDelta:Record<string,number>;wheelDelta:Record<string,number>}
export interface InputActionMap{name:string;priority:number;bindings:{actionId:string;bindingId:string;control:string;valueType:string}[]}
export interface InputAction{id:string;valueType:string;value:unknown;phase:number|string}
export interface InputSnapshot{raw?:{pressedKeys:string[];pressedMouseButtons:string[];pointerPosition:Record<string,number>;pointerDelta:Record<string,number>;wheelDelta:Record<string,number>;sources:InputSource[]};actions?:{maps:InputActionMap[];actions:InputAction[]};captureLease?:{remainingMilliseconds:number};lastInputAt?:string}
export interface ResourceNode{key:string;type:string;state:string;generation:number;memoryBytes:number;referenceCount:number;id:string;error?:string;isReference?:boolean;referenceTo?:string;dependencies:ResourceNode[];referrers?:string[]}
export interface ResourceSnapshot{catalog:{key:string;type:string}[];roots:ResourceNode[];allLoaded:ResourceNode[];activeOperation?:unknown}
export interface Tag{key:string;value:string}
export interface MetricSample{timestamp:string;measurement:number}
export interface MetricSeries{key:string;meter:string;instrument:string;kind:string;unit?:string;description?:string;tags:Tag[];samples:MetricSample[];current?:number;delta?:number;ratePerSecond?:number;count?:number;sum?:number;min?:number;max?:number;p50?:number;p95?:number}
export interface ActivityItem{activityId:string;traceId:string;parentId?:string;source:string;operation:string;kind:string;start:string;durationMilliseconds:number;status:string;statusDescription?:string;isActive:boolean;tags:Tag[];baggage:Tag[];events:{timestamp:string;name:string;tags:Tag[]}[]}
export interface DiagnosticsSnapshot{status:Record<string,number|boolean>;activities:ActivityItem[];metrics:MetricSeries[]}
export type OperationStatus='queued'|'running'|'succeeded'|'failed'|'canceled';
export interface Operation{ id:string;name:string;target:string;status:OperationStatus;startedAt:string;finishedAt?:string;durationMs?:number;result?:unknown;error?:ProtocolError;retry?:()=>void }
