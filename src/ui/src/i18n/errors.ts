import { RpcError } from '@/rpc/protocol';
import { en } from './en';
import { translate, type ResourceKey } from './translate';

/**
 * Turns a host failure into a sentence.
 *
 * The host sends `{ code, args }` and never prose, so this is the only place an error becomes
 * words. An unrecognised code falls back to a generic message rather than leaking the
 * developer-facing text across into the interface.
 */
export function describeError(error: unknown): string {
  if (!(error instanceof RpcError)) {
    return translate('error.unknown_error');
  }

  const known = error.code in en.error;
  const key = (known ? `error.${error.code}` : 'error.unknown_error') as ResourceKey;

  if (!known) {
    console.error(`[i18n] No message for error code '${error.code}'`, error);
  }

  return translate(key, error.args);
}
