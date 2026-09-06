import { en } from '../../../src/ui/src/i18n/en.ts';

/**
 * The application's own string catalogue, imported rather than duplicated.
 *
 * CLAUDE.md forbids hardcoded user-facing strings, and a test suite that pasted the English
 * copy would quietly become a second, drifting catalogue. Importing it means these tests assert
 * on *behaviour* — that the right resource is rendered — and that deleting a key is a compile
 * error here too.
 */
export { en };

/** Substitutes `{placeholder}` the way the renderer's own `translate` does. */
export function fill(template: string, args: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (whole, key: string) =>
    key in args ? String(args[key]) : whole,
  );
}
