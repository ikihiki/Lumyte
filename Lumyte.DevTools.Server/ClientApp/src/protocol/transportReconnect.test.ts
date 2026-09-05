import {expect, it} from 'vitest';
import {DevToolsTransport} from './transport';

class DelayedCloseSocket extends EventTarget {
  readyState = WebSocket.OPEN;
  sent: string[] = [];
  constructor() { super(); queueMicrotask(() => this.dispatchEvent(new Event('open'))); }
  send(value: string) { this.sent.push(value); }
  close() { /* The browser may deliver the close event after a replacement opens. */ }
}

it('ignores the old socket closing while a replacement request is pending', async () => {
  const sockets: DelayedCloseSocket[] = [];
  const transport = new DevToolsTransport(() => {
    const socket = new DelayedCloseSocket();
    sockets.push(socket);
    return socket as unknown as WebSocket;
  });
  await transport.connect();
  await transport.connect();
  const request = transport.hosts();
  const id = JSON.parse(sockets[1].sent[0]).id;

  sockets[0].dispatchEvent(new CloseEvent('close', {code: 1000}));
  sockets[0].dispatchEvent(new MessageEvent('message', {data: JSON.stringify({id, result: ['stale']})}));
  sockets[1].dispatchEvent(new MessageEvent('message', {data: JSON.stringify({id, result: []})}));

  await expect(request).resolves.toEqual([]);
  transport.close();
});
