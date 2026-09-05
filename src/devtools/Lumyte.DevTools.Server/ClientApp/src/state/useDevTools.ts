import {useCallback, useEffect, useRef, useState} from 'react';
import {DevToolsTransport} from '../protocol/transport';
import type {ConnectionStage, DiagnosticsSnapshot, DomainInfo, HostInfo, InputSnapshot, Operation, ProtocolError, ResourceSnapshot} from '../protocol/types';

const emptyDiagnostics: DiagnosticsSnapshot = {status: {}, activities: [], metrics: []};
const emptyResources: ResourceSnapshot = {catalog: [], roots: [], allLoaded: []};

export function useDevTools(transport?: DevToolsTransport) {
  const [client] = useState(() => transport ?? new DevToolsTransport());
  const generation = useRef(0);
  const selectedHost = useRef('');
  const subscriptions = useRef(new Set<string>());
  const refreshSequence = useRef(0);
  const retryTimer = useRef<number | undefined>(undefined);
  const connectRef = useRef<(manual?: boolean, desiredHost?: string) => Promise<void>>(async () => {});
  const [stage, setStage] = useState<ConnectionStage>('idle');
  const [reason, setReason] = useState('');
  const [hosts, setHosts] = useState<HostInfo[]>([]);
  const [hostId, setHostIdState] = useState('');
  const [domains, setDomains] = useState<DomainInfo[]>([]);
  const [input, setInput] = useState<InputSnapshot>({});
  const [resources, setResources] = useState(emptyResources);
  const [diagnostics, setDiagnostics] = useState(emptyDiagnostics);
  const [operations, setOperations] = useState<Operation[]>([]);
  const [attempts, setAttempts] = useState(0);
  const [lastMessage, setLastMessage] = useState<Date>();

  const track = useCallback(async <T,>(name: string, target: string, run: () => Promise<T>) => {
    const token = generation.current;
    const id = crypto.randomUUID(), start = performance.now(), startedAt = new Date().toISOString();
    setOperations(v => [{id, name, target, status: 'running' as const, startedAt}, ...v].slice(0, 100));
    try {
      const result = await run();
      if (token !== generation.current) throw {code: 'canceled', message: 'Host connection changed.', retryable: true};
      setOperations(v => v.map(x => x.id === id ? {...x, status: 'succeeded', finishedAt: new Date().toISOString(), durationMs: performance.now() - start, result} : x));
      return result;
    } catch (value) {
      const error = value as ProtocolError;
      if (token === generation.current) {
        setOperations(v => v.map(x => x.id === id ? {...x, status: error.code === 'canceled' ? 'canceled' : 'failed', finishedAt: new Date().toISOString(), durationMs: performance.now() - start, error} : x));
      }
      throw error;
    }
  }, []);

  const refresh = useCallback(async (selected = selectedHost.current, token = generation.current) => {
    if (!selected) return;
    const sequence = ++refreshSequence.current;
    const [d, i, r, diag] = await Promise.all([
      client.domains(selected),
      client.invoke<InputSnapshot>(selected, 'input', 'getState', 'query'),
      client.invoke<ResourceSnapshot>(selected, 'resources', 'getState', 'query'),
      client.invoke<DiagnosticsSnapshot>(selected, 'diagnostics', 'getSnapshot', 'query'),
    ]);
    if (token !== generation.current || selected !== selectedHost.current || sequence !== refreshSequence.current) return;
    setDomains(d);
    setInput(i);
    setResources(r);
    setDiagnostics(diag);
  }, [client]);

  const connect = useCallback(async (manual = false, desiredHost = selectedHost.current) => {
    window.clearTimeout(retryTimer.current);
    const token = ++generation.current;
    subscriptions.current.clear();
    setOperations(v => v.map(x => x.status === 'running' ? {...x, status: 'canceled', finishedAt: new Date().toISOString()} : x));
    setStage('server-connecting');
    setReason('');
    try {
      await client.connect();
      if (token !== generation.current) return;
      setStage('server-connected');
      setStage('negotiating');
      const negotiation = await client.negotiate();
      if (token !== generation.current) return;
      if (!negotiation.supportedVersions.includes('1.0')) {
        setStage('incompatible');
        setReason(`Server supports ${negotiation.supportedVersions.join(', ')}`);
        return;
      }
      const available = await client.hosts();
      if (token !== generation.current) return;
      setHosts(available);
      const selected = desiredHost
        ? available.find(h => h.hostId === desiredHost)?.hostId
        : available[0]?.hostId;
      if (!selected) {
        selectedHost.current = '';
        setHostIdState('');
        setDomains([]);
        setInput({});
        setResources(emptyResources);
        setDiagnostics(emptyDiagnostics);
        setStage('host-selecting');
        setReason(desiredHost ? 'The selected runtime host is not connected.' : 'No runtime host is connected.');
        return;
      }
      selectedHost.current = selected;
      setHostIdState(selected);
      await refresh(selected, token);
      if (token !== generation.current) return;
      for (const [domain, feature] of [['input', 'inputChanged'], ['resources', 'operationChanged'], ['diagnostics', 'updated']]) {
        const subscription = await client.subscribe(selected, domain, feature);
        if (token !== generation.current) return;
        subscriptions.current.add(subscription.subscriptionId);
      }
      setStage('host-connected');
      setAttempts(0);
    } catch (value) {
      if (token !== generation.current) return;
      setStage('disconnected');
      setReason((value as ProtocolError).message ?? String(value));
      if (!manual) {
        setAttempts(v => {
          if (token !== generation.current) return v;
          const next = v + 1;
          retryTimer.current = window.setTimeout(() => {
            if (token === generation.current) void connectRef.current(false, desiredHost);
          }, Math.min(30000, 500 * 2 ** Math.min(next, 6)));
          return next;
        });
      }
    }
  }, [client, refresh]);

  useEffect(() => { connectRef.current = connect; }, [connect]);

  const selectHost = useCallback(async (id: string) => {
    generation.current++;
    client.close('host switch');
    selectedHost.current = id;
    setHostIdState(id);
    setDomains([]);
    setInput({});
    setResources(emptyResources);
    setDiagnostics(emptyDiagnostics);
    setLastMessage(undefined);
    await connect(true, id);
  }, [client, connect]);

  const invoke = useCallback(<T,>(domain: string, feature: string, kind: 'query' | 'command', params: unknown = {}) =>
    track(`${domain}/${feature}`, hostId, () => client.invoke<T>(hostId, domain, feature, kind, params)), [client, hostId, track]);

  useEffect(() => {
    let active = true;
    const event = (e: Event) => {
      const value = (e as CustomEvent).detail;
      if (value.hostId !== selectedHost.current || !subscriptions.current.has(value.subscriptionId)) return;
      setLastMessage(new Date());
      if (value.domain === 'input') setInput(value.params.snapshot);
      if (value.domain === 'resources') setResources(value.params.snapshot);
      if (value.domain === 'diagnostics') setDiagnostics({status: value.params.status, activities: value.params.activities, metrics: value.params.metrics});
    };
    client.addEventListener('protocol-event', event);
    queueMicrotask(() => { if (active) void connect(); });
    return () => {
      active = false;
      generation.current++;
      subscriptions.current.clear();
      window.clearTimeout(retryTimer.current);
      client.removeEventListener('protocol-event', event);
      client.close();
    };
  }, [client, connect]);

  return {stage, reason, hosts, hostId, domains, input, resources, diagnostics, operations, attempts, lastMessage,
    connect: () => connect(true), selectHost, refresh: () => refresh(), invoke, setInput, setResources, setDiagnostics};
}
