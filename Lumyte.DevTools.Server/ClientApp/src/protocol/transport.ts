import type {DomainInfo,Envelope,HostInfo,Negotiation,ProtocolError} from './types';
export type TransportEvent=(event:NonNullable<Envelope['event']>)=>void;
export class DevToolsTransport extends EventTarget{
  private socket?:WebSocket; private nextId=1; private pending=new Map<number,{resolve:(v:unknown)=>void;reject:(e:ProtocolError)=>void}>();
  lastMessageAt?:Date; reconnectAttempts=0;
  constructor(private readonly factory:(url:string)=>WebSocket=(url)=>new WebSocket(url)){super()}
  async connect(signal?:AbortSignal){this.close('reconnect');const scheme=location.protocol==='https:'?'wss':'ws';const ws=this.factory(`${scheme}://${location.host}/devtools`);this.socket=ws;await new Promise<void>((resolve,reject)=>{ws.addEventListener('open',()=>resolve(),{once:true});ws.addEventListener('error',()=>reject(new Error('WebSocket connection failed')),{once:true});signal?.addEventListener('abort',()=>{ws.close();reject(signal.reason)},{once:true})});ws.addEventListener('message',e=>this.receive(String(e.data)));ws.addEventListener('close',e=>this.disconnect(`Socket closed (${e.code}${e.reason?`: ${e.reason}`:''})`));}
  request<T>(message:Record<string,unknown>,signal?:AbortSignal){if(this.socket?.readyState!==WebSocket.OPEN)return Promise.reject({code:'not_connected',message:'Server is not connected.',retryable:true} satisfies ProtocolError);const id=this.nextId++;return new Promise<T>((resolve,reject)=>{this.pending.set(id,{resolve:v=>resolve(v as T),reject});signal?.addEventListener('abort',()=>{this.pending.delete(id);reject({code:'canceled',message:'Operation canceled.',retryable:true})},{once:true});this.socket!.send(JSON.stringify({id,...message}));});}
  hosts(signal?:AbortSignal){return this.request<HostInfo[]>({method:'hosts'},signal)}
  negotiate(signal?:AbortSignal){return this.request<Negotiation>({method:'negotiate',protocolVersion:'1.0',capabilities:['subscriptions','operations','diagnostics-v1']},signal)}
  domains(hostId:string,signal?:AbortSignal){return this.request<DomainInfo[]>({method:'domains',hostId},signal)}
  invoke<T>(hostId:string,domain:string,feature:string,kind:'query'|'command',params:unknown={},signal?:AbortSignal){return this.request<T>({method:'invoke',hostId,domain,feature,kind,params},signal)}
  subscribe(hostId:string,domain:string,feature:string,signal?:AbortSignal){return this.request<{subscriptionId:string}>({method:'subscribe',hostId,domain,feature},signal)}
  unsubscribe(subscriptionId:string){return this.request({method:'unsubscribe',subscriptionId})}
  close(reason='client closed'){this.socket?.close(1000,reason);this.socket=undefined;this.disconnect(reason)}
  private receive(text:string){this.lastMessageAt=new Date();const envelope=JSON.parse(text) as Envelope;if(envelope.event){this.dispatchEvent(new CustomEvent('protocol-event',{detail:envelope.event}));return}if(envelope.id===undefined)return;const pending=this.pending.get(envelope.id);if(!pending)return;this.pending.delete(envelope.id);if(envelope.error)pending.reject({...envelope.error,retryable:envelope.error.retryable??['not_connected','host_not_found','remote_error'].includes(envelope.error.code)});else pending.resolve(envelope.result)}
  private disconnect(reason:string){for(const item of this.pending.values())item.reject({code:'disconnected',message:reason,retryable:true});this.pending.clear();this.dispatchEvent(new CustomEvent('transport-close',{detail:reason}))}
}
