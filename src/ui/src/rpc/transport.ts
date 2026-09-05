/**
 * The message channel the host and the renderer share.
 *
 * Photino exposes exactly two primitives on `window.external`. Everything else the bridge
 * does — correlation, typing, notifications — is built on top of them, and is testable
 * because this interface can be faked.
 */
export interface RpcTransport {
  send(message: string): void;
  subscribe(handler: (message: string) => void): () => void;
}

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

/** True when running inside the Photino WebView rather than a browser or jsdom. */
export function isPhotinoHost(): boolean {
  const candidate = (globalThis as { external?: Partial<PhotinoExternal> }).external;
  return typeof candidate?.sendMessage === 'function' && typeof candidate?.receiveMessage === 'function';
}

/**
 * Photino's `receiveMessage` has no unsubscribe, so it is registered once and messages are
 * fanned out to subscribers here.
 */
export function createPhotinoTransport(): RpcTransport {
  const external = (globalThis as unknown as { external: PhotinoExternal }).external;
  const handlers = new Set<(message: string) => void>();
  let registered = false;

  return {
    send(message) {
      external.sendMessage(message);
    },
    subscribe(handler) {
      if (!registered) {
        registered = true;
        external.receiveMessage((message) => {
          for (const current of handlers) {
            current(message);
          }
        });
      }

      handlers.add(handler);
      return () => {
        handlers.delete(handler);
      };
    },
  };
}
