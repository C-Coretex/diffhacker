import type { RpcErrorData } from '@/contracts';

/** JSON-RPC 2.0 wire shapes. Only the members this bridge actually uses. */

export type JsonRpcId = number | string;

export interface JsonRpcRequest {
  jsonrpc: '2.0';
  id?: JsonRpcId;
  method: string;
  params?: unknown;
}

export interface JsonRpcErrorBody {
  code: number;
  message: string;
  data?: unknown;
}

export interface JsonRpcResponse {
  jsonrpc: '2.0';
  id: JsonRpcId | null;
  result?: unknown;
  error?: JsonRpcErrorBody;
}

export function isJsonRpcResponse(value: unknown): value is JsonRpcResponse {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<JsonRpcResponse>;
  return candidate.jsonrpc === '2.0' && ('result' in candidate || 'error' in candidate);
}

export function isJsonRpcNotification(value: unknown): value is Required<Pick<JsonRpcRequest, 'method'>> & JsonRpcRequest {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<JsonRpcRequest>;
  return candidate.jsonrpc === '2.0' && typeof candidate.method === 'string' && candidate.id === undefined;
}

/**
 * A failure returned by the host.
 *
 * The host never sends user-facing prose across the bridge, so `code` — not `message` — is
 * what the interface renders, resolved through the string catalogue. `message` is developer
 * detail that belongs in the console and in `log.txt`.
 */
export class RpcError extends Error {
  constructor(
    readonly rpcCode: number,
    developerMessage: string,
    readonly data?: RpcErrorData,
  ) {
    super(developerMessage);
    this.name = 'RpcError';
  }

  /** Stable error identifier the renderer resolves to a message, when the host sent one. */
  get code(): string {
    return this.data?.code ?? 'unknown_error';
  }

  get args(): Record<string, string> {
    return this.data?.args ?? {};
  }
}

export function toRpcErrorData(data: unknown): RpcErrorData | undefined {
  if (typeof data !== 'object' || data === null) {
    return undefined;
  }

  const candidate = data as Partial<RpcErrorData>;
  return typeof candidate.code === 'string' ? (candidate as RpcErrorData) : undefined;
}
