import { afterEach, describe, expect, it, vi } from 'vitest';
import { RpcClient } from './client';
import { RpcError } from './protocol';
import { FakeTransport } from '@/test/fakeTransport';

describe('RpcClient', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('sends a JSON-RPC 2.0 request with positional parameters', async () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);

    const pending = client.call<string>('host.ping', { steps: 3 });
    const request = transport.lastRequest();

    expect(request).toMatchObject({
      jsonrpc: '2.0',
      method: 'host.ping',
      params: [{ steps: 3 }],
    });
    expect(typeof request.id).toBe('number');

    transport.respond('pong');
    await expect(pending).resolves.toBe('pong');
  });

  it('correlates concurrent requests by id', async () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);

    const first = client.call<string>('a');
    const firstId = transport.lastRequest().id;
    const second = client.call<string>('b');
    const secondId = transport.lastRequest().id;

    // Answer out of order: correlation must not depend on arrival order.
    transport.receive({ jsonrpc: '2.0', id: secondId, result: 'second' });
    transport.receive({ jsonrpc: '2.0', id: firstId, result: 'first' });

    await expect(first).resolves.toBe('first');
    await expect(second).resolves.toBe('second');
  });

  it('rejects with the contract error code, not the developer message', async () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);

    const pending = client.call('demo.startCountdown', { steps: 0 });
    transport.respondWithError('demo_steps_out_of_range', { steps: '0' });

    const error = await pending.catch((reason: unknown) => reason);
    expect(error).toBeInstanceOf(RpcError);
    expect((error as RpcError).code).toBe('demo_steps_out_of_range');
    expect((error as RpcError).args).toEqual({ steps: '0' });
  });

  it('delivers notifications to subscribers in order and stops after unsubscribe', () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);
    const seen: number[] = [];

    const unsubscribe = client.on<{ step: number }>('demo/progress', (params) => seen.push(params.step));

    transport.notify('demo/progress', { step: 0 });
    transport.notify('demo/progress', { step: 1 });
    unsubscribe();
    transport.notify('demo/progress', { step: 2 });

    expect(seen).toEqual([0, 1]);
  });

  it('times out a request the host never answers', async () => {
    vi.useFakeTimers();
    const transport = new FakeTransport();
    const client = new RpcClient(transport, { timeoutMs: 1_000, onProtocolError: () => {} });

    // Attach the handler before advancing, so the rejection is never momentarily unobserved.
    const settled = client.call('host.ping').catch((reason: unknown) => reason);
    await vi.advanceTimersByTimeAsync(1_001);

    const error = await settled;
    expect(error).toBeInstanceOf(RpcError);
    expect((error as RpcError).code).toBe('rpc_timeout');
  });

  it('discards malformed messages without disturbing pending requests', async () => {
    const transport = new FakeTransport();
    const onProtocolError = vi.fn();
    const client = new RpcClient(transport, { onProtocolError });

    const pending = client.call<string>('host.ping');
    transport.receive('this is not json');
    transport.receive({ hello: 'world' });

    expect(onProtocolError).toHaveBeenCalledTimes(2);

    transport.respond('pong');
    await expect(pending).resolves.toBe('pong');
  });

  it('rejects outstanding requests when disposed', async () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);

    const pending = client.call('host.ping');
    client.dispose();

    await expect(pending).rejects.toThrow(/disposed/i);
  });
});
