import { en, type Catalogue } from './en';

/**
 * Every dotted path through the catalogue that ends at a string. `t('demo.stpe')` is a
 * compile error, not a blank label.
 */
type Leaves<T> = {
  [K in keyof T & string]: T[K] extends string ? K : `${K}.${Leaves<T[K]>}`;
}[keyof T & string];

export type ResourceKey = Leaves<Catalogue>;

export type ResourceArgs = Record<string, string | number>;

function lookup(key: string): string | undefined {
  let current: unknown = en;

  for (const segment of key.split('.')) {
    if (typeof current !== 'object' || current === null) {
      return undefined;
    }

    current = (current as Record<string, unknown>)[segment];
  }

  return typeof current === 'string' ? current : undefined;
}

/**
 * Resolves a resource key, substituting `{name}` placeholders.
 *
 * An unknown key returns the key itself rather than throwing: a missing string should be
 * visible and diagnosable, never a blank panel or a crash mid-render.
 */
export function translate(key: ResourceKey, args?: ResourceArgs): string {
  const template = lookup(key);
  if (template === undefined) {
    console.error(`[i18n] Missing resource key: ${key}`);
    return key;
  }

  if (!args) {
    return template;
  }

  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = args[name];
    return value === undefined ? match : String(value);
  });
}
