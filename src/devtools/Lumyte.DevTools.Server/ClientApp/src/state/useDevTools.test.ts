import {act, renderHook, waitFor} from '@testing-library/react';
import {describe, expect, it} from 'vitest';
import {DevToolsTransport} from '../protocol/transport';
import type {DiagnosticsSnapshot, InputSnapshot, ResourceSnapshot} from '../protocol/types';
import {useDevTools} from './useDevTools';

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return {promise, resolve};
}

class FakeTransport extends DevToolsTransport {
  subscriptions: {host: string; id: string}[] = [];
  delayedInput?: Promise<InputSnapshot>;
  override async connect() {}
  override close() {}
  override async negotiate() {
    return {protocolVersion: '1.0', supportedVersions: ['1.0'], capabilities: [], heartbeatIntervalMilliseconds: 5000};
  }
  override async hosts() { return ['A', 'B'].map(hostId => ({hostId, displayName: hostId, connected: true})); }
  override async domains(host: string) { return [{name: host, features: []}]; }
  override async invoke<T>(host: string, domain: string): Promise<T> {
    if (host === 'A' && domain === 'input' && this.delayedInput) return await this.delayedInput as T;
    const snapshot: InputSnapshot | ResourceSnapshot | DiagnosticsSnapshot = domain === 'input'
      ? {lastInputAt: host}
      : domain === 'resources' ? {catalog: [{key: host, type: 'test'}], roots: [], allLoaded: []}
        : {status: {[host]: true}, activities: [], metrics: []};
    return snapshot as T;
  }
  override async subscribe(host: string) {
    const id = String(this.subscriptions.length);
    this.subscriptions.push({host, id});
    return {subscriptionId: id};
  }
}

describe('useDevTools', () => {
  it('selects subscribes and displays the requested host', async () => {
    const transport = new FakeTransport();
    const {result} = renderHook(() => useDevTools(transport));
    await waitFor(() => expect(result.current.stage).toBe('host-connected'));

    await act(() => result.current.selectHost('B'));

    expect(result.current.hostId).toBe('B');
    expect(result.current.input.lastInputAt).toBe('B');
    expect(result.current.domains).toEqual([{name: 'B', features: []}]);
    expect(transport.subscriptions.slice(-3).map(x => x.host)).toEqual(['B', 'B', 'B']);
  });

  it('ignores a refresh that completes after switching hosts', async () => {
    const transport = new FakeTransport();
    const {result} = renderHook(() => useDevTools(transport));
    await waitFor(() => expect(result.current.stage).toBe('host-connected'));
    const delayed = deferred<InputSnapshot>();
    transport.delayedInput = delayed.promise;
    let refresh!: Promise<void>;
    act(() => { refresh = result.current.refresh(); });

    await act(() => result.current.selectHost('B'));
    await act(async () => { delayed.resolve({lastInputAt: 'late A'}); await refresh; });

    expect(result.current.input.lastInputAt).toBe('B');
    expect(result.current.diagnostics.status).toEqual({B: true});
  });

  it('ignores events from an old subscription after reconnecting to the same host', async () => {
    const transport = new FakeTransport();
    const {result} = renderHook(() => useDevTools(transport));
    await waitFor(() => expect(result.current.stage).toBe('host-connected'));
    const old = transport.subscriptions[0];
    await act(() => result.current.connect());

    act(() => transport.dispatchEvent(new CustomEvent('protocol-event', {detail: {
      hostId: 'A', subscriptionId: old.id, domain: 'input', params: {snapshot: {lastInputAt: 'stale'}},
    }})));

    expect(result.current.input.lastInputAt).toBe('A');
  });
});
