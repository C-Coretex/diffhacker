import type { RpcTransport } from '@/rpc/transport';

/**
 * An in-memory stand-in for the Photino message channel, so the bridge can be tested without
 * a WebView.
 */
export class FakeTransport implements RpcTransport {
  readonly sent: string[] = [];

  private readonly handlers = new Set<(message: string) => void>();

  send(message: string): void {
    this.sent.push(message);
  }

  subscribe(handler: (message: string) => void): () => void {
    this.handlers.add(handler);
    return () => {
      this.handlers.delete(handler);
    };
  }

  /** Simulates the host pushing a message to the renderer. */
  receive(message: unknown): void {
    const raw = typeof message === 'string' ? message : JSON.stringify(message);
    for (const handler of [...this.handlers]) {
      handler(raw);
    }
  }

  /** The most recent request the renderer sent, parsed. */
  lastRequest<T = { id: number; method: string; params: unknown[] }>(): T {
    const raw = this.sent.at(-1);
    if (raw === undefined) {
      throw new Error('No request has been sent.');
    }

    return JSON.parse(raw) as T;
  }

  /** Answers the most recent request with a successful result. */
  respond(result: unknown): void {
    const { id } = this.lastRequest();
    this.receive({ jsonrpc: '2.0', id, result });
  }

  /** Answers the most recent request with an error carrying a contract error code. */
  respondWithError(code: string, args?: Record<string, string>): void {
    const { id } = this.lastRequest();
    this.receive({
      jsonrpc: '2.0',
      id,
      error: { code: -32000, message: 'developer detail', data: { code, args } },
    });
  }

  notify(method: string, params: unknown): void {
    this.receive({ jsonrpc: '2.0', method, params });
  }
}
