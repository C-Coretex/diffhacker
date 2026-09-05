import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { RpcClient } from './client';
import { createPhotinoTransport, isPhotinoHost, type RpcTransport } from './transport';

const RpcContext = createContext<RpcClient | null>(null);

interface RpcProviderProps {
  children: ReactNode;
  /** Injected by tests; production resolves the Photino channel. */
  transport?: RpcTransport;
}

/**
 * Owns the single RPC client for the window's lifetime.
 *
 * When there is no host — a browser, or jsdom in a unit test — the client is `null` rather
 * than a stub that pretends to work. Callers handle the detached case explicitly.
 */
export function RpcProvider({ children, transport }: RpcProviderProps) {
  const [client, setClient] = useState<RpcClient | null>(null);

  const resolved = useMemo(() => transport ?? (isPhotinoHost() ? createPhotinoTransport() : null), [transport]);

  useEffect(() => {
    if (!resolved) {
      setClient(null);
      return;
    }

    const created = new RpcClient(resolved);
    setClient(created);
    return () => created.dispose();
  }, [resolved]);

  return <RpcContext.Provider value={client}>{children}</RpcContext.Provider>;
}

/** The active client, or `null` when the renderer is not running inside the host window. */
export function useRpc(): RpcClient | null {
  return useContext(RpcContext);
}
