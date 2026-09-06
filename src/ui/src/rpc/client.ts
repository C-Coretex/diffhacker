import {
  isJsonRpcNotification,
  isJsonRpcResponse,
  RpcError,
  toRpcErrorData,
  type JsonRpcId,
  type JsonRpcRequest,
} from './protocol';
import type { RpcTransport } from './transport';

const DEFAULT_TIMEOUT_MS = 30_000;

/**
 * The standard JSON-RPC cancellation notification. StreamJsonRpc answers it out of the box on
 * the host side, cancelling the `CancellationToken` of the in-flight call.
 */
const CANCEL_METHOD = '$/cancelRequest';

interface Pending {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
  /** Removes the entry and detaches any abort listener. */
  onSettled: () => void;
}

export interface RpcClientOptions {
  /** How long a request waits before it is abandoned. */
  timeoutMs?: number;
  /** Where protocol-level problems are reported. Defaults to `console`. */
  onProtocolError?: (message: string, detail: unknown) => void;
}

/**
 * Typed JSON-RPC 2.0 client over the host bridge.
 *
 * Parameters are always sent positionally, as an array. JSON-RPC permits named parameters
 * too, but positional binding is unambiguous on the StreamJsonRpc side and keeps the typed
 * wrappers in `methods.ts` honest.
 */
export class RpcClient {
  private readonly pending = new Map<JsonRpcId, Pending>();
  private readonly notificationHandlers = new Map<string, Set<(params: never) => void>>();
  private readonly unsubscribe: () => void;
  private readonly timeoutMs: number;
  private readonly onProtocolError: (message: string, detail: unknown) => void;

  private nextId = 1;
  private disposed = false;

  constructor(
    private readonly transport: RpcTransport,
    options: RpcClientOptions = {},
  ) {
    this.timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    this.onProtocolError =
      options.onProtocolError ??
      ((message, detail) => {
        console.error(`[rpc] ${message}`, detail);
      });

    this.unsubscribe = transport.subscribe((message) => this.receive(message));
  }

  /** Invokes a host method and resolves with its typed result. */
  call<TResult>(method: string, ...params: unknown[]): Promise<TResult> {
    return this.callWithTimeout<TResult>(method, this.timeoutMs, ...params);
  }

  /**
   * Invokes a host method that the caller can abort.
   *
   * Aborting sends `$/cancelRequest`, so the host stops the work rather than finishing it for
   * an answer nobody is waiting for. That matters for anything long: a `git diff` over a cold
   * working tree, or an LLM run that costs money for every second it keeps going.
   */
  callAbortable<TResult>(
    method: string,
    signal: AbortSignal,
    timeoutMs: number,
    ...params: unknown[]
  ): Promise<TResult> {
    return this.invoke<TResult>(method, timeoutMs, signal, params);
  }

  /**
   * Invokes a host method with a deadline of its own.
   *
   * Most calls answer in milliseconds and the default is generous. Loading the changeset of a
   * very large repository is genuinely slow — several full `git diff` passes over a cold
   * working tree — and abandoning it at thirty seconds would report a timeout for work that was
   * going to succeed.
   */
  callWithTimeout<TResult>(method: string, timeoutMs: number, ...params: unknown[]): Promise<TResult> {
    return this.invoke<TResult>(method, timeoutMs, undefined, params);
  }

  private invoke<TResult>(
    method: string,
    timeoutMs: number,
    signal: AbortSignal | undefined,
    params: unknown[],
  ): Promise<TResult> {
    if (this.disposed) {
      return Promise.reject(new Error('The RPC client has been disposed.'));
    }

    if (signal?.aborted) {
      return Promise.reject(new RpcError(0, `'${method}' was cancelled before it was sent.`, {
        code: 'rpc_cancelled',
        args: { method },
      }));
    }

    const id = this.nextId++;
    const request: JsonRpcRequest = { jsonrpc: '2.0', id, method, params };

    return new Promise<TResult>((resolve, reject) => {
      let onAbort: (() => void) | undefined;

      const settle = (): void => {
        this.pending.delete(id);
        if (onAbort) {
          signal?.removeEventListener('abort', onAbort);
        }
      };

      const timer = setTimeout(() => {
        settle();

        // Tell the host to stop as well. Without this the renderer gives up while the work
        // grinds on: a git pass keeps running, an LLM run keeps spending.
        this.cancel(id);

        reject(new RpcError(0, `The host did not answer '${method}' within ${timeoutMs}ms.`, {
          code: 'rpc_timeout',
          args: { method },
        }));
      }, timeoutMs);

      if (signal) {
        onAbort = () => {
          clearTimeout(timer);
          settle();
          this.cancel(id);

          reject(new RpcError(0, `'${method}' was cancelled.`, {
            code: 'rpc_cancelled',
            args: { method },
          }));
        };

        signal.addEventListener('abort', onAbort, { once: true });
      }

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timer,
        onSettled: settle,
      });

      this.transport.send(JSON.stringify(request));
    });
  }

  /** Asks the host to abandon a request that is still in flight. */
  private cancel(id: JsonRpcId): void {
    this.transport.send(
      JSON.stringify({ jsonrpc: '2.0', method: CANCEL_METHOD, params: { id } }),
    );
  }

  /** Subscribes to a server-to-client notification. Returns an unsubscribe function. */
  on<TParams>(method: string, handler: (params: TParams) => void): () => void {
    let handlers = this.notificationHandlers.get(method);
    if (!handlers) {
      handlers = new Set();
      this.notificationHandlers.set(method, handlers);
    }

    handlers.add(handler as (params: never) => void);
    return () => {
      handlers.delete(handler as (params: never) => void);
    };
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    this.unsubscribe();

    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.onSettled();
      pending.reject(new Error('The RPC client was disposed before the host answered.'));
    }

    this.pending.clear();
    this.notificationHandlers.clear();
  }

  private receive(raw: string): void {
    let message: unknown;
    try {
      message = JSON.parse(raw);
    } catch (error) {
      this.onProtocolError('Discarded a message that was not JSON', { raw, error });
      return;
    }

    if (isJsonRpcNotification(message)) {
      this.dispatchNotification(message.method, message.params);
      return;
    }

    if (!isJsonRpcResponse(message) || message.id === null) {
      this.onProtocolError('Discarded a message that was not a JSON-RPC response', message);
      return;
    }

    const pending = this.pending.get(message.id);
    if (!pending) {
      this.onProtocolError('Received a response with no matching request', message);
      return;
    }

    clearTimeout(pending.timer);
    pending.onSettled();

    if (message.error) {
      pending.reject(
        new RpcError(message.error.code, message.error.message, toRpcErrorData(message.error.data)),
      );
      return;
    }

    pending.resolve(message.result);
  }

  private dispatchNotification(method: string, params: unknown): void {
    const handlers = this.notificationHandlers.get(method);
    if (!handlers || handlers.size === 0) {
      return;
    }

    for (const handler of handlers) {
      try {
        (handler as (value: unknown) => void)(params);
      } catch (error) {
        this.onProtocolError(`A handler for '${method}' threw`, error);
      }
    }
  }
}
