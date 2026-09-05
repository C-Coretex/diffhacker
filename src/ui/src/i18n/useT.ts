import { useCallback } from 'react';
import { translate, type ResourceArgs, type ResourceKey } from './translate';

/**
 * The hook every component uses to render text.
 *
 * It is deliberately a hook rather than a bare import: when a real localisation layer
 * arrives it will need the active locale from context, and every call site is already
 * shaped for it.
 */
export function useT(): (key: ResourceKey, args?: ResourceArgs) => string {
  return useCallback((key: ResourceKey, args?: ResourceArgs) => translate(key, args), []);
}
