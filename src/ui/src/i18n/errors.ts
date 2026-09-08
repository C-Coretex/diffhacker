import { RpcError } from '@/rpc/protocol';
import { en } from './en';
import { translate, type ResourceKey } from './translate';

/**
 * Turns a host failure into a sentence.
 *
 * The host sends `{ code, args }` and never prose, so this is the only place an error becomes
 * words. An unrecognised code falls back to a generic message rather than leaking the
 * developer-facing text across into the interface.
 *
 * Two catalogues, searched in order. `error` holds the codes the host raises for itself; a run
 * that reached a provider and stopped there fails with an `llm_*` code instead, and those live in
 * `runFailure` because the same codes are also shown live while a run is still going.
 */
export function describeError(error: unknown): string {
  if (!(error instanceof RpcError)) {
    return translate('error.unknown_error');
  }

  const group = error.code in en.error ? 'error' : error.code in en.runFailure ? 'runFailure' : undefined;
  const key = (group ? `${group}.${error.code}` : 'error.unknown_error') as ResourceKey;

  if (!group) {
    console.error(`[i18n] No message for error code '${error.code}'`, error);
  }

  return translate(key, error.args);
}
